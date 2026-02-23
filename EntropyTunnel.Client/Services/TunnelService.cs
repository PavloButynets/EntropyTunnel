using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using EntropyTunnel.Client.Configuration;
using EntropyTunnel.Client.Dashboard;
using EntropyTunnel.Client.Models;
using EntropyTunnel.Client.Multiplexer;
using EntropyTunnel.Client.Pipeline;

namespace EntropyTunnel.Client.Services;

/// <summary>
/// BackgroundService that owns the WebSocket lifecycle.
/// Runs concurrently with the Kestrel web server (dashboard on :4040).
///   - Connect / auto-reconnect to the remote EntropyTunnel.Server
///   - Receive incoming request commands from the server
///   - Dispatch each command through the RequestPipeline (fire-and-forget per request)
///   - Send responses via TunnelMultiplexer (0x01/0x02/0x03 protocol)
///   - Keep the connection alive with heartbeat pings
///   - Log completed requests to RuleStore for the Inspector UI
/// </summary>
/// 
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
    private readonly TunnelStatusService _status;
    private readonly ILogger<TunnelService> _logger;

    public TunnelService(
        TunnelSettings settings,
        TunnelMultiplexer mux,
        RequestPipeline pipeline,
        RuleStore ruleStore,
        TunnelStatusService status,
        ILogger<TunnelService> logger)
    {
        _settings = settings;
        _mux = mux;
        _pipeline = pipeline;
        _ruleStore = ruleStore;
        _status = status;
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
                _status.SetDisconnected();
                _logger.LogWarning("Connection lost: {Msg}. Retrying in 3 s…", ex.Message);
                await Task.Delay(3_000, stoppingToken);
            }
        }
    }


    private async Task RunConnectionAsync(CancellationToken ct)
    {
        string serverUrl = $"{_settings.ServerUrl}?clientId={_settings.ClientId}";
        string publicUrl = $"http://{_settings.ClientId}.{_settings.PublicDomain}/";

        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        _logger.LogInformation("Connecting to {Url}…", serverUrl);
        await ws.ConnectAsync(new Uri(serverUrl), ct);

        _mux.AttachWebSocket(ws);
        _status.SetConnected(publicUrl);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅  Tunnel established!");
        Console.WriteLine($"🌍  Public URL : {publicUrl}");
        Console.WriteLine($"🖥️   Dashboard  : http://localhost:{_settings.DashboardPort}/\n");
        Console.ResetColor();

        try
        {
            _ = HeartbeatAsync(ct);

            var buffer = new byte[64 * 1024];
            var activeRequests = new ConcurrentDictionary<Guid, IncomingRequest>();

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // Accumulate all WebSocket frames that belong to a single logical message.
                // A single SendAsync on the server uses endOfMessage=true, but the WebSocket
                // layer may still fragment large payloads (big cookie/auth headers) into
                // multiple transport frames - only the last one has EndOfMessage=true.
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
            _status.SetDisconnected();
        }
    }

    private async Task HandleIncomingRequestAsync(IncomingRequest incoming, CancellationToken ct)
    {
        TunnelContext? ctx = null;
        try
        {
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

            // - Capture body preview for the Inspector (up to 4 KB)
            string? bodyPreview = null;
            long? contentLength = null;
            if (incoming.BodyStream is { Length: > 0 } bodyMs)
            {
                contentLength = bodyMs.Length;
                bodyMs.Seek(0, SeekOrigin.Begin);
                var previewLen = (int)Math.Min(4096, bodyMs.Length);
                var previewBuf = new byte[previewLen];
                _ = await bodyMs.ReadAsync(previewBuf, ct);
                bodyPreview = Encoding.UTF8.GetString(previewBuf);
            }

            // Flatten multi-value response headers to string for display
            Dictionary<string, string>? responseHeadersForLog = null;
            if (ctx.ResponseHeaders is { Count: > 0 })
                responseHeadersForLog = ctx.ResponseHeaders
                    .ToDictionary(kvp => kvp.Key, kvp => string.Join(", ", kvp.Value));

            var color = ctx.StatusCode < 300 ? ConsoleColor.Gray
                      : ctx.StatusCode < 400 ? ConsoleColor.Yellow
                      : ConsoleColor.Red;

            Console.ForegroundColor = color;
            Console.WriteLine($"  {incoming.Method,-6} {incoming.Path,-45} {ctx.StatusCode}  [{ctx.Stopwatch.ElapsedMilliseconds}ms]" +
                              (ctx.AppliedChaosRule is not null ? $"  ⚡ {ctx.AppliedChaosRule}" : "") +
                              (ctx.AppliedMockRule is not null ? $"  🎭 {ctx.AppliedMockRule}" : ""));
            Console.ResetColor();

            _ruleStore.LogRequest(new RequestLogEntry
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
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request");
        }
        finally
        {
            if (ctx?.ResponseStream is Stream s) await s.DisposeAsync();
            incoming.BodyStream?.Dispose();
        }
    }



    private async Task HandlePacketAsync(byte[] packet, CancellationToken ct)
    {
        TunnelContext? ctx = null;
        try
        {
            var requestId = new Guid(packet[..16]);
            string command = Encoding.UTF8.GetString(packet, 16, packet.Length - 16);
            var parts = command.Split(' ', 2);

            if (parts.Length < 2)
            {
                _logger.LogWarning("Malformed command: '{Cmd}'", command);
                return;
            }

            string method = parts[0];
            string path = parts[1];

            ctx = new TunnelContext
            {
                RequestId = requestId,
                Method = method,
                Path = path
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
            var color = ctx.StatusCode < 300 ? ConsoleColor.Gray
                      : ctx.StatusCode < 400 ? ConsoleColor.Yellow
                      : ConsoleColor.Red;

            Console.ForegroundColor = color;
            Console.WriteLine($"  {method,-6} {path,-45} {ctx.StatusCode}  [{ctx.Stopwatch.ElapsedMilliseconds}ms]" +
                              (ctx.AppliedChaosRule is not null ? $"  ⚡ {ctx.AppliedChaosRule}" : "") +
                              (ctx.AppliedMockRule is not null ? $"  🎭 {ctx.AppliedMockRule}" : ""));
            Console.ResetColor();

            _ruleStore.LogRequest(new RequestLogEntry
            {
                RequestId = requestId,
                Method = method,
                Path = path,
                StatusCode = ctx.StatusCode,
                DurationMs = ctx.Stopwatch.ElapsedMilliseconds,
                AppliedChaosRule = ctx.AppliedChaosRule,
                AppliedMockRule = ctx.AppliedMockRule,
                ResolvedTargetUrl = ctx.TargetUrl
            });
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request");
        }
        finally
        {
            if (ctx?.ResponseStream is Stream s)
                await s.DisposeAsync();
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
            catch { break; } // connection dropped — outer loop handles reconnect
        }
    }


    private void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║     EntropyTunnel Agent  v1.0            ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine($"  Port   : {_settings.LocalPort}");
        Console.WriteLine($"  ID     : {_settings.ClientId}");
        Console.WriteLine($"  Server : {_settings.ServerUrl}");
        Console.WriteLine();
    }
}
