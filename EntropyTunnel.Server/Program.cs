using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

WebSocket? _agentSocket = null;
var _pendingRequests = new ConcurrentDictionary<Guid, TaskCompletionSource<(byte[] Data, string ContentType, int StatusCode)>>();
var _socketSendLock = new SemaphoreSlim(1, 1);

app.Map("/tunnel", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest();

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    _agentSocket = ws;
    Console.WriteLine("[Server] ✅ AGENT CONNECTED!");

    var buffer = new byte[1024 * 1024];

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

            // [PROTOCOL v2]: 
            // [GUID (16)] + [Status(4)] + [TypeLen(4)] + [TypeString] + [Data]

            int offset = 0;
            if (result.Count > 24) // Header size check
            {
                // 1. ID
                var idBytes = new byte[16];
                Array.Copy(buffer, offset, idBytes, 0, 16);
                var id = new Guid(idBytes);
                offset += 16;

                // 2. HTTP Status Code (int)
                int statusCode = BitConverter.ToInt32(buffer, offset);
                offset += 4;

                // 3. Content Type Length (int)
                int typeLen = BitConverter.ToInt32(buffer, offset);
                offset += 4;

                // 4. Content Type String
                string contentType = Encoding.UTF8.GetString(buffer, offset, typeLen);
                offset += typeLen;

                // 5. Real Data
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
    catch (Exception ex) { Console.WriteLine($"[Tunnel Error] {ex.Message}"); }
    finally
    {
        _agentSocket = null;
        _pendingRequests.Clear();
        Console.WriteLine("[Server] ❌ DISCONNECTED");
    }
    return Results.Ok();
});

app.Map("{*path}", async (HttpContext context, string? path) =>
{
    if (_agentSocket == null || _agentSocket.State != WebSocketState.Open)
        return Results.Content("Tunnel Offline", "text/plain", Encoding.UTF8, 503);

    var requestId = Guid.NewGuid();
    var tcs = new TaskCompletionSource<(byte[] Data, string ContentType, int StatusCode)>();
    _pendingRequests.TryAdd(requestId, tcs);

    string targetPath = path ?? "";
    string command = $"{context.Request.Method} /{targetPath}{context.Request.QueryString}";
    byte[] commandBytes = Encoding.UTF8.GetBytes(command);

    byte[] packet = new byte[16 + commandBytes.Length];
    Array.Copy(requestId.ToByteArray(), 0, packet, 0, 16);
    Array.Copy(commandBytes, 0, packet, 16, commandBytes.Length);

    await _socketSendLock.WaitAsync();
    try { await _agentSocket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, CancellationToken.None); }
    finally { _socketSendLock.Release(); }

    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(30000));

    if (completedTask == tcs.Task)
    {
        var result = await tcs.Task;

        if (result.StatusCode != 200)
        {
            context.Response.StatusCode = result.StatusCode;
        }

        return Results.Bytes(result.Data, contentType: result.ContentType);
    }
    else
    {
        _pendingRequests.TryRemove(requestId, out _);
        return Results.StatusCode(504);
    }
});

app.Run();