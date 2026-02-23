using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var _connectedAgents = new ConcurrentDictionary<string, WebSocket>();
var _agentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

// Store requests while waiting for the Header (0x01).
var _pendingRequests = new ConcurrentDictionary<Guid, TaskCompletionSource<(string ContentType, int StatusCode, ChannelReader<byte[]> BodyReader, Dictionary<string, string[]> Headers)>>();
// Store channels for active streams (0x02)
var _activeChannels = new ConcurrentDictionary<Guid, Channel<byte[]>>();

// --- WebSocket Endpoint ---
app.Map("/tunnel", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest();

    string? clientId = context.Request.Query["clientId"];
    if (string.IsNullOrEmpty(clientId)) return Results.BadRequest("Missing clientId");

    using var ws = await context.WebSockets.AcceptWebSocketAsync();

    _connectedAgents[clientId] = ws;
    _agentLocks.TryAdd(clientId, new SemaphoreSlim(1, 1));
    Console.WriteLine($"[Server] ✅ AGENT CONNECTED: {clientId}");

    var receiveBuffer = new byte[1024 * 64];

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                ms.Write(receiveBuffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            var packet = ms.ToArray();

            // Ignore empty packets or keep-alive signals (0x00)
            if (packet.Length < 17 && packet[0] == 0x00) continue;

            var idBytes = new byte[16];
            Array.Copy(packet, 0, idBytes, 0, 16);
            var id = new Guid(idBytes);

            byte packetType = packet[16];

            if (packetType == 0x01) // Header
            {
                int statusCode = BitConverter.ToInt32(packet, 17);
                int typeLen = BitConverter.ToInt32(packet, 21);
                string contentType = Encoding.UTF8.GetString(packet, 25, typeLen);

                // Parse response headers — agent sends Dictionary<string, string[]>
                // so that multi-value headers (Set-Cookie, WWW-Authenticate…) are preserved.
                int headersJsonLen = BitConverter.ToInt32(packet, 25 + typeLen);
                string headersJson = Encoding.UTF8.GetString(packet, 29 + typeLen, headersJsonLen);
                var headers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(headersJson)
                    ?? new Dictionary<string, string[]>();

                var channel = Channel.CreateUnbounded<byte[]>();
                _activeChannels[id] = channel;

                if (_pendingRequests.TryRemove(id, out var tcs))
                    tcs.SetResult((contentType, statusCode, channel.Reader, headers));
            }
            else if (packetType == 0x02) // Data Chunk
            {
                if (_activeChannels.TryGetValue(id, out var channel))
                {
                    var dataChunk = new byte[packet.Length - 17];
                    Array.Copy(packet, 17, dataChunk, 0, dataChunk.Length);
                    await channel.Writer.WriteAsync(dataChunk);
                }
            }
            else if (packetType == 0x03) // End of Stream
            {
                if (_activeChannels.TryRemove(id, out var channel))
                {
                    channel.Writer.Complete();
                }
            }
        }
    }
    catch (Exception ex) { Console.WriteLine($"[Error {clientId}] {ex.Message}"); }
    finally
    {
        _connectedAgents.TryRemove(clientId, out _);
        _agentLocks.TryRemove(clientId, out _);
    }

    return Results.Empty;
});

// --- HTTP Proxy ---
app.Map("{*path}", async (HttpContext context, string? path) =>
{
    string host = context.Request.Host.Host;
    string clientId = host.Split('.')[0];

    if (char.IsDigit(clientId[0]) || clientId == "localhost")
    {
        return Results.Ok($"Entropy Tunnel v0.9 FULL-PROXY. Usage: http://<client-id>.{context.Request.Host.Value}/");
    }

    if (!_connectedAgents.TryGetValue(clientId, out var agentSocket) || agentSocket.State != WebSocketState.Open)
    {
        return Results.Content($"Tunnel '{clientId}' is offline.", "text/plain", Encoding.UTF8, 404);
    }

    var requestId = Guid.NewGuid();
    var tcs = new TaskCompletionSource<(string ContentType, int StatusCode, ChannelReader<byte[]> BodyReader, Dictionary<string, string[]> Headers)>();
    _pendingRequests.TryAdd(requestId, tcs);

    string targetPath = path ?? "";
    string fullPath = $"/{targetPath}{context.Request.QueryString}";

    // Forward all request headers with two targeted exclusions:
    //   Host             - HttpClient on the agent sets this from the target URL;
    //                      forwarding the tunnel's public hostname would break virtual-host
    //                      checks and CORS validation on the local service.
    //   Transfer-Encoding - describes THIS transport hop; the body is fully buffered before
    //                      forwarding, so the original encoding no longer applies downstream.
    var requestHeaders = new Dictionary<string, string>();
    foreach (var header in context.Request.Headers)
    {
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
        if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
        requestHeaders[header.Key] = string.Join(", ", header.Value.ToArray());
    }

    bool hasBody = context.Request.ContentLength > 0
                || context.Request.Headers.ContainsKey("Transfer-Encoding");
    var requestMeta = new
    {
        Method = context.Request.Method,
        Path = fullPath,
        Headers = requestHeaders,
        HasBody = hasBody,
    };

    string metaJson = JsonSerializer.Serialize(requestMeta);
    byte[] metaBytes = Encoding.UTF8.GetBytes(metaJson);
    byte[] headerPacket = new byte[16 + 1 + 4 + metaBytes.Length];

    Array.Copy(requestId.ToByteArray(), 0, headerPacket, 0, 16);
    headerPacket[16] = 0x10; // New packet type: Request Header
    Array.Copy(BitConverter.GetBytes(metaBytes.Length), 0, headerPacket, 17, 4);
    Array.Copy(metaBytes, 0, headerPacket, 21, metaBytes.Length);

    if (_agentLocks.TryGetValue(clientId, out var lockSlim))
    {
        await lockSlim.WaitAsync();
        try
        {
            await agentSocket.SendAsync(new ArraySegment<byte>(headerPacket),
                WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        finally { lockSlim.Release(); }
    }

    // Stream request body if present
    if (requestMeta.HasBody && context.Request.Body.CanRead)
    {
        const int chunkSize = 16 * 1024;
        byte[] buffer = new byte[chunkSize];
        int bytesRead;

        while ((bytesRead = await context.Request.Body.ReadAsync(buffer)) > 0)
        {
            byte[] bodyChunk = new byte[16 + 1 + bytesRead];
            Array.Copy(requestId.ToByteArray(), 0, bodyChunk, 0, 16);
            bodyChunk[16] = 0x11; // Packet type: Request Body Chunk
            Array.Copy(buffer, 0, bodyChunk, 17, bytesRead);

            if (_agentLocks.TryGetValue(clientId, out lockSlim))
            {
                await lockSlim.WaitAsync();
                try
                {
                    await agentSocket.SendAsync(new ArraySegment<byte>(bodyChunk),
                        WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                finally { lockSlim.Release(); }
            }
        }
    }

    // Send end-of-request marker
    byte[] eofPacket = new byte[17];
    Array.Copy(requestId.ToByteArray(), 0, eofPacket, 0, 16);
    eofPacket[16] = 0x12; // Packet type: Request EOF

    if (_agentLocks.TryGetValue(clientId, out lockSlim))
    {
        await lockSlim.WaitAsync();
        try
        {
            await agentSocket.SendAsync(new ArraySegment<byte>(eofPacket),
                WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        finally { lockSlim.Release(); }
    }

    // Wait for response
    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(30000));

    if (completedTask == tcs.Task)
    {
        var result = await tcs.Task;
        context.Response.StatusCode = result.StatusCode;
        // Content-Type is carried separately in result.ContentType (set by the agent).
        // Headers dict never contains Content-Type, so just assign it directly.
        context.Response.ContentType = result.ContentType;

        // Forward all response headers.
        foreach (var header in result.Headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            context.Response.Headers[header.Key] =
                new Microsoft.Extensions.Primitives.StringValues(header.Value);
        }

        await foreach (var chunk in result.BodyReader.ReadAllAsync())
        {
            await context.Response.Body.WriteAsync(chunk);
            await context.Response.Body.FlushAsync();
        }

        return Results.Empty;
    }
    else
    {
        _pendingRequests.TryRemove(requestId, out _);
        _activeChannels.TryRemove(requestId, out _);
        return Results.StatusCode(504);
    }
});

app.Run();