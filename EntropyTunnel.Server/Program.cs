using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels; // Обов'язково для стрімінгу

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var _connectedAgents = new ConcurrentDictionary<string, WebSocket>();
var _agentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

// Store requests while waiting fot the Header (0x01)
var _pendingRequests = new ConcurrentDictionary<Guid, TaskCompletionSource<(string ContentType, int StatusCode, ChannelReader<byte[]> BodyReader)>>();
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

                var channel = Channel.CreateUnbounded<byte[]>();
                _activeChannels[id] = channel;

                if (_pendingRequests.TryRemove(id, out var tcs))
                    tcs.SetResult((contentType, statusCode, channel.Reader));
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
        return Results.Ok($"Entropy Tunnel v0.8 MULTIPLEXED. Usage: http://<client-id>.{context.Request.Host.Value}/");
    }

    if (!_connectedAgents.TryGetValue(clientId, out var agentSocket) || agentSocket.State != WebSocketState.Open)
    {
        return Results.Content($"Tunnel '{clientId}' is offline.", "text/plain", Encoding.UTF8, 404);
    }

    var requestId = Guid.NewGuid();
    var tcs = new TaskCompletionSource<(string ContentType, int StatusCode, ChannelReader<byte[]> BodyReader)>();
    _pendingRequests.TryAdd(requestId, tcs);

    string targetPath = path ?? "";
    string command = $"{context.Request.Method} /{targetPath}{context.Request.QueryString}";

    byte[] commandBytes = Encoding.UTF8.GetBytes(command);
    byte[] packet = new byte[16 + commandBytes.Length];
    Array.Copy(requestId.ToByteArray(), 0, packet, 0, 16);
    Array.Copy(commandBytes, 0, packet, 16, commandBytes.Length);

    if (_agentLocks.TryGetValue(clientId, out var lockSlim))
    {
        await lockSlim.WaitAsync();
        try { await agentSocket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, CancellationToken.None); }
        finally { lockSlim.Release(); }
    }

    // Wait for the first pachet(Header) (Заголовок 0x01)
    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(30000));

    if (completedTask == tcs.Task)
    {
        var result = await tcs.Task;
        context.Response.StatusCode = result.StatusCode;
        context.Response.ContentType = result.ContentType;

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
        _activeChannels.TryRemove(requestId, out _); // Очистка при таймауті
        return Results.StatusCode(504);
    }
});

app.Run();