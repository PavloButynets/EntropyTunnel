using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace EntropyTunnel.Server.Handlers;

public static class HttpProxyHandler
{
    public static void MapHttpProxy(this WebApplication app)
    {
        app.Map("{*path}", Handle);
    }

    private static async Task<IResult> Handle(HttpContext context, string? path, TunnelHub hub)
    {
        string host = context.Request.Host.Host;
        // clientId lives in the subdomain: myapp.entropy-tunnel.xyz -> "myapp"
        string clientId = host.Split('.')[0];

        // IP addresses and localhost mean someone hit the server directly, not through a tunnel subdomain
        if (char.IsDigit(clientId[0]) || clientId == "localhost")
            return Results.Ok($"Entropy Tunnel v2.0. Usage: https://<client-id>.{context.Request.Host.Value}/");

        if (!hub.Connections.TryGetValue(clientId, out var conn) || conn.Socket.State != WebSocketState.Open)
            return Results.Content($"Tunnel '{clientId}' is offline.", "text/plain", Encoding.UTF8, 404);

        var requestId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<AgentResponse>();
        hub.PendingRequests.TryAdd(requestId, tcs);

        string fullPath = $"/{path ?? ""}{context.Request.QueryString}";

        var requestHeaders = new Dictionary<string, string>();
        foreach (var header in context.Request.Headers)
        {
            // Host would confuse the local service; Transfer-Encoding conflicts with how we stream the body
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

        // Packet layout: [16B requestId][0x10][4B jsonLen][JSON]
        string metaJson = JsonSerializer.Serialize(requestMeta);
        byte[] metaBytes = Encoding.UTF8.GetBytes(metaJson);
        var headerPacket = new byte[16 + 1 + 4 + metaBytes.Length];
        Array.Copy(requestId.ToByteArray(), 0, headerPacket, 0, 16);
        headerPacket[16] = 0x10; // request header frame type
        Array.Copy(BitConverter.GetBytes(metaBytes.Length), 0, headerPacket, 17, 4);
        Array.Copy(metaBytes, 0, headerPacket, 21, metaBytes.Length);
        await hub.SendToAgentAsync(clientId, headerPacket);

        if (hasBody && context.Request.Body.CanRead)
        {
            const int chunkSize = 16 * 1024;
            var buffer = new byte[chunkSize];
            int bytesRead;
            while ((bytesRead = await context.Request.Body.ReadAsync(buffer)) > 0)
            {
                // [16B requestId][0x11][raw bytes]
                var bodyChunk = new byte[16 + 1 + bytesRead];
                Array.Copy(requestId.ToByteArray(), 0, bodyChunk, 0, 16);
                bodyChunk[16] = 0x11; // body chunk
                Array.Copy(buffer, 0, bodyChunk, 17, bytesRead);
                await hub.SendToAgentAsync(clientId, bodyChunk);
            }
        }

        // [16B requestId][0x12] - tells the agent the request body is fully sent
        var eofPacket = new byte[17];
        Array.Copy(requestId.ToByteArray(), 0, eofPacket, 0, 16);
        eofPacket[16] = 0x12;
        await hub.SendToAgentAsync(clientId, eofPacket);

        // Wait for the agent to send back 0x01 response header; 30s is generous for a local service
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(30_000));
        if (completedTask != tcs.Task)
        {
            hub.PendingRequests.TryRemove(requestId, out _);
            hub.ActiveChannels.TryRemove(requestId, out _);
            return Results.StatusCode(504);
        }

        var (contentType, statusCode, bodyReader, headers) = await tcs.Task;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = contentType;

        foreach (var header in headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            // StringValues preserves multi-value headers (e.g. Set-Cookie) as separate lines
            context.Response.Headers[header.Key] = new StringValues(header.Value);
        }

        await foreach (var chunk in bodyReader.ReadAllAsync())
        {
            await context.Response.Body.WriteAsync(chunk);
            await context.Response.Body.FlushAsync();
        }

        return Results.Empty;
    }
}
