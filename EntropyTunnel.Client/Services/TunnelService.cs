using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using EntropyTunnel.Client.Configuration;
using EntropyTunnel.Client.State;
using EntropyTunnel.Core;
using EntropyTunnel.Core.Models;
using EntropyTunnel.Core.Payloads;
using EntropyTunnel.Client.Multiplexer;
using EntropyTunnel.Client.Pipeline;

namespace EntropyTunnel.Client.Services;

/// <summary>
/// BackgroundService that owns the WebSocket lifecycle.
///   - Connect / auto-reconnect to EntropyTunnel.Server
///   - Handle 0x20 SyncRules: atomically update local RuleStore
///   - Handle 0x22 SessionAuth: print ngrok-style connection banner
///   - Dispatch 0x10/0x11/0x12 request frames through RequestPipeline
///   - Send 0x21 LogEvent to the server after each request (replaces local log)
///   - Keep the connection alive with heartbeat pings
/// </summary>
public record RequestMetadata(string Method, string Path, Dictionary<string, string> Headers, bool HasBody);

public record IncomingRequest
{
    public Guid RequestId { get; init; }
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public Dictionary<string, string> Headers { get; init; } = new();
    public MemoryStream? BodyStream { get; init; }
}

public sealed class TunnelService : BackgroundService
{
    private readonly TunnelSettings _settings;
    private readonly TunnelMultiplexer _mux;
    private readonly RequestPipeline _pipeline;
    private readonly RuleStore _ruleStore;
    private readonly ILogger<TunnelService> _logger;

    public TunnelService(
        TunnelSettings settings,
        TunnelMultiplexer mux,
        RequestPipeline pipeline,
        RuleStore ruleStore,
        ILogger<TunnelService> logger)
    {
        _settings = settings;
        _mux = mux;
        _pipeline = pipeline;
        _ruleStore = ruleStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PrintBanner();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERR  Connection lost: {ex.Message} — retrying in 3s");
                Console.ResetColor();
                await Task.Delay(3_000, stoppingToken);
            }
        }
    }

    private async Task RunConnectionAsync(CancellationToken ct)
    {
        string serverUrl = $"{_settings.ServerUrl}?clientId={_settings.ClientId}&accountId={_settings.AccountId}";

        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        _logger.LogInformation("Connecting to {Url}…", serverUrl);
        await ws.ConnectAsync(new Uri(serverUrl), ct);

        _mux.AttachWebSocket(ws);

        try
        {
            _ = HeartbeatAsync(ct);

            var buffer = new byte[64 * 1024];
            var activeRequests = new ConcurrentDictionary<Guid, IncomingRequest>();

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // Accumulate all WebSocket frames that belong to a single logical message.
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Server closed the WebSocket.");
                    break;
                }

                if (ms.Length < 17) continue;

                var packet = ms.ToArray();

                // Control frames (Guid.Empty prefix)
                if (TunnelMultiplexer.TryParseControlFrame(packet, out byte ctrlType, out string ctrlJson))
                {
                    if (ctrlType == ControlFrame.SyncRules)
                    {
                        var payload = TunnelMultiplexer.DeserializePayload<SyncRulesPayload>(ctrlJson);
                        if (payload is not null)
                        {
                            _ruleStore.ApplySync(payload);
                            _logger.LogDebug("Rules synced: {Chaos} chaos, {Mock} mock, {Routing} routing.",
                                payload.ChaosRules.Count, payload.MockRules.Count, payload.RoutingRules.Count);
                        }
                    }
                    else if (ctrlType == ControlFrame.SessionAuth)
                    {
                        var payload = TunnelMultiplexer.DeserializePayload<SessionAuthPayload>(ctrlJson);
                        if (payload is not null) PrintSessionAuthBanner(payload);
                    }
                    continue;
                }

                // Request frames (non-zero request ID)
                var requestId = new Guid(packet[..16]);
                byte packetType = packet[16];

                if (packetType == 0x10) // Request Header
                {
                    int metaLen = BitConverter.ToInt32(packet, 17);
                    string metaJson = Encoding.UTF8.GetString(packet, 21, metaLen);
                    var meta = JsonSerializer.Deserialize<RequestMetadata>(metaJson);

                    if (meta is not null)
                    {
                        var incoming = new IncomingRequest
                        {
                            RequestId = requestId,
                            Method = meta.Method,
                            Path = meta.Path,
                            Headers = meta.Headers,
                            BodyStream = meta.HasBody ? new MemoryStream() : null
                        };
                        activeRequests[requestId] = incoming;
                    }
                }
                else if (packetType == 0x11) // Request Body Chunk
                {
                    if (activeRequests.TryGetValue(requestId, out var incoming) && incoming.BodyStream is not null)
                    {
                        int chunkLen = packet.Length - 17;
                        await incoming.BodyStream.WriteAsync(packet.AsMemory(17, chunkLen), ct);
                    }
                }
                else if (packetType == 0x12) // Request EOF
                {
                    if (activeRequests.TryRemove(requestId, out var incoming))
                    {
                        incoming.BodyStream?.Seek(0, SeekOrigin.Begin);
                        _ = HandleIncomingRequestAsync(incoming, ct);
                    }
                }
            }
        }
        finally
        {
            _mux.DetachWebSocket();
        }
    }

    private async Task HandleIncomingRequestAsync(IncomingRequest incoming, CancellationToken ct)
    {
        TunnelContext? ctx = null;
        try
        {
            // Capture body preview (first 2 KB) before the pipeline consumes the stream
            string? bodyPreview = null;
            if (incoming.BodyStream is { Length: > 0 } bs)
            {
                int previewLen = (int)Math.Min(2048, bs.Length);
                byte[] previewBuf = new byte[previewLen];
                _ = bs.Read(previewBuf, 0, previewLen);
                bs.Seek(0, SeekOrigin.Begin);
                bodyPreview = Encoding.UTF8.GetString(previewBuf);
            }

            ctx = new TunnelContext
            {
                RequestId = incoming.RequestId,
                Method = incoming.Method,
                Path = incoming.Path,
                RequestHeaders = incoming.Headers,
                RequestBody = incoming.BodyStream
            };

            await _pipeline.ExecuteAsync(ctx, ct);

            if (ctx.ResponseStream is not null)
            {
                await _mux.SendResponseAsync(
                    ctx.RequestId,
                    ctx.StatusCode,
                    ctx.ContentType,
                    ctx.ResponseStream,
                    ctx.ResponseHeaders ?? new Dictionary<string, string[]>(),
                    ct);
            }

            ctx.Stopwatch.Stop();

            long? contentLength = incoming.BodyStream is { Length: > 0 } s ? s.Length : null;

            Dictionary<string, string>? responseHeadersForLog = null;
            if (ctx.ResponseHeaders is { Count: > 0 })
                responseHeadersForLog = ctx.ResponseHeaders
                    .ToDictionary(kvp => kvp.Key, kvp => string.Join(", ", kvp.Value));

            LogRequest(incoming.Method, incoming.Path, ctx.StatusCode, ctx.Stopwatch.ElapsedMilliseconds,
                       ctx.AppliedChaosRule, ctx.AppliedMockRule);

            await _mux.SendLogEventAsync(new RequestLogEntry
            {
                RequestId = incoming.RequestId,
                Method = incoming.Method,
                Path = incoming.Path,
                StatusCode = ctx.StatusCode,
                DurationMs = ctx.Stopwatch.ElapsedMilliseconds,
                AppliedChaosRule = ctx.AppliedChaosRule,
                AppliedMockRule = ctx.AppliedMockRule,
                ResolvedTargetUrl = ctx.TargetUrl,
                RequestHeaders = incoming.Headers,
                RequestBodyPreview = bodyPreview,
                RequestContentLength = contentLength,
                ResponseHeaders = responseHeadersForLog,
            }, ct);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERR  {incoming.Method} {incoming.Path} — {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            if (ctx?.ResponseStream is Stream s) await s.DisposeAsync();
            incoming.BodyStream?.Dispose();
        }
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _mux.State == WebSocketState.Open)
        {
            try
            {
                await Task.Delay(5_000, ct);
                await _mux.SendPingAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    private void PrintBanner()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"  ████ █  █ ████ ███  ███  ███  █ █");
        Console.WriteLine(@"  █    ██ █  █   █ █  █ █  █ █  █ █");
        Console.WriteLine(@"  ███  █ ██  █   ███  █ █  ███   █ ");
        Console.WriteLine(@"  █    █  █  █   █ █  █ █  █     █ ");
        Console.WriteLine(@"  ████ █  █  █   █  █ ███  █     █ ");
        Console.WriteLine();
        Console.WriteLine(@"   ████ █  █ █  █ █  █ ████ █   ");
        Console.WriteLine(@"    █   █  █ █  █ ██ █ █    █   ");
        Console.WriteLine(@"    █   █  █ █  █ █ ██ ███  █   ");
        Console.WriteLine(@"    █   █  █ █  █ █  █ █    █   ");
        Console.WriteLine(@"    █   ███  ███  █  █ ████ ████");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(@"                              v1.0  Tunnel Agent");
        Console.ResetColor();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Client   :  {_settings.ClientId}");
        Console.WriteLine($"  Port     :  {_settings.LocalPort}");
        Console.WriteLine($"  Server   :  {_settings.ServerUrl}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private void PrintSessionAuthBanner(SessionAuthPayload payload)
    {
        string sep = new string('─', 54);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  {sep}");
        Console.WriteLine($"  Tunnel ready");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  Tunnel URL   →  https://{_settings.ClientId}.{_settings.PublicDomain}/");
        Console.WriteLine($"  Dashboard    →  {payload.DashboardUrl}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  Password     →  {payload.Password}");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Account      →  {_settings.AccountId}");
        Console.WriteLine($"  Local Port   →  {_settings.LocalPort}");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  {sep}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void LogRequest(
        string method, string path, int statusCode, long elapsedMs,
        string? chaosRule, string? mockRule)
    {
        Console.ForegroundColor = statusCode < 300 ? ConsoleColor.Green
                                : statusCode < 500 ? ConsoleColor.Yellow
                                : ConsoleColor.Red;

        Console.Write($"[{DateTime.Now:HH:mm:ss}] {method,-7}{path,-45}{statusCode}  {elapsedMs}ms");

        if (chaosRule is not null || mockRule is not null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            if (chaosRule is not null) Console.Write($"  chaos:{chaosRule}");
            if (mockRule is not null) Console.Write($"  mock:{mockRule}");
        }

        Console.WriteLine();
        Console.ResetColor();
    }
}
