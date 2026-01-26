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

    string? clientId = context.Request.Query["clientId"];
    if (string.IsNullOrEmpty(clientId)) return Results.BadRequest("Missing clientId");

    using var ws = await context.WebSockets.AcceptWebSocketAsync();

    _connectedAgents[clientId] = ws;
    _agentLocks.TryAdd(clientId, new SemaphoreSlim(1, 1));
    Console.WriteLine($"[Server] ✅ AGENT CONNECTED: {clientId}");

    var buffer = new byte[1024 * 1024]; // 1MB buffer
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

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
                    tcs.SetResult((content, contentType, statusCode));
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

// --- 2. HTTP Proxy (via NIP.IO) ---
app.Map("{*path}", async (HttpContext context, string? path) =>
{
    // Host: "server.13.60.182.126.nip.io:8080"
    string host = context.Request.Host.Host;

    string clientId = host.Split('.')[0];


    if (char.IsDigit(clientId[0]) || clientId == "localhost")
    {
        return Results.Ok($"Entropy Tunnel v7.0. Usage: http://<client-id>.{context.Request.Host.Value}/");
    }

    if (!_connectedAgents.TryGetValue(clientId, out var agentSocket) || agentSocket.State != WebSocketState.Open)
    {
        return Results.Content($"Tunnel '{clientId}' is offline.", "text/plain", Encoding.UTF8, 404);
    }

    var requestId = Guid.NewGuid();
    var tcs = new TaskCompletionSource<(byte[] Data, string ContentType, int StatusCode)>();
    _pendingRequests.TryAdd(requestId, tcs);

    string targetPath = path ?? "";
    string command = $"{context.Request.Method} /{targetPath}{context.Request.QueryString}";

    Console.WriteLine($"[Proxy] {clientId} -> {command}");

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

    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(30000)); // 30 sec timeout

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

app.Run();