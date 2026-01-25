using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var _connectedAgents = new ConcurrentDictionary<string, WebSocket>();
var _pendingRequests = new ConcurrentDictionary<Guid, TaskCompletionSource<(byte[] Data, string ContentType, int StatusCode)>>();
var _agentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

app.Map("/tunnel", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest();

    string clientId = context.Request.Query["clientId"].ToString();
    if (string.IsNullOrEmpty(clientId)) return Results.BadRequest("Missing clientId");

    using var ws = await context.WebSockets.AcceptWebSocketAsync();

    _connectedAgents[clientId] = ws;
    _agentLocks.TryAdd(clientId, new SemaphoreSlim(1, 1));

    Console.WriteLine($"[Server] ✅ AGENT CONNECTED: {clientId}");

    var buffer = new byte[1024 * 1024];

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

            // [PROTOCOL v2 PARSING]
            if (result.Count > 24)
            {
                int offset = 0;
                var idBytes = new byte[16];
                Array.Copy(buffer, offset, idBytes, 0, 16);
                var id = new Guid(idBytes);
                offset += 16;

                int statusCode = BitConverter.ToInt32(buffer, offset); offset += 4;
                int typeLen = BitConverter.ToInt32(buffer, offset); offset += 4;
                string contentType = Encoding.UTF8.GetString(buffer, offset, typeLen); offset += typeLen;

                int dataSize = result.Count - offset;
                var content = new byte[dataSize];
                Array.Copy(buffer, offset, content, 0, dataSize);

                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    tcs.SetResult((content, contentType, statusCode));
                }
            }
        }
    }
    catch (Exception ex) { Console.WriteLine($"[Tunnel Error {clientId}] {ex.Message}"); }
    finally
    {
        _connectedAgents.TryRemove(clientId, out _);
        Console.WriteLine($"[Server] ❌ DISCONNECTED: {clientId}");
    }

    return Results.Empty;
});

app.Map("{clientId}/{*path}", async (HttpContext context, string clientId, string? path) =>
{
    if (!_connectedAgents.TryGetValue(clientId, out var agentSocket) || agentSocket.State != WebSocketState.Open)
    {
        return Results.Content($"Agent '{clientId}' is offline or not found.", "text/plain", Encoding.UTF8, 404);
    }

    var requestId = Guid.NewGuid();
    var tcs = new TaskCompletionSource<(byte[] Data, string ContentType, int StatusCode)>();
    _pendingRequests.TryAdd(requestId, tcs);

    string targetPath = path ?? "";
    string command = $"{context.Request.Method} /{targetPath}{context.Request.QueryString}";

    Console.WriteLine($"[Router] {clientId} -> {command}");

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

    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(30000));

    if (completedTask == tcs.Task)
    {
        var result = await tcs.Task;
        if (result.StatusCode != 200) context.Response.StatusCode = result.StatusCode;
        return Results.Bytes(result.Data, contentType: result.ContentType);
    }
    else
    {
        _pendingRequests.TryRemove(requestId, out _);
        return Results.StatusCode(504);
    }
});

app.Map("/", () => Results.Ok("Entropy Tunnel Relay v7.0 (Multi-Agent). Use /{clientId}/..."));

app.Run();