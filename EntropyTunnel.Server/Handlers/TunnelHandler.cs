using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EntropyTunnel.Core;
using EntropyTunnel.Core.Models;
using EntropyTunnel.Core.Payloads;
using EntropyTunnel.Server.Sse;
using EntropyTunnel.Server.State;

namespace EntropyTunnel.Server.Handlers;

public static class TunnelHandler
{
    public static void MapTunnel(this WebApplication app)
    {
        app.Map("/tunnel", Handle);
    }

    private static async Task<IResult> Handle(HttpContext context, TunnelHub hub, AgentStateStore stateStore, SseConnectionManager sseMgr, IConfiguration config)
    {
        if (!context.WebSockets.IsWebSocketRequest)
            return Results.BadRequest("WebSocket connection required.");

        string? clientId = context.Request.Query["clientId"];
        if (string.IsNullOrEmpty(clientId))
            return Results.BadRequest("Missing clientId query parameter.");

        if (hub.Connections.ContainsKey(clientId))
        {
            Console.WriteLine($"[Server] Duplicate clientId rejected: '{clientId}' is already connected");
            return Results.Conflict(new { error = "ClientId already in use", clientId });
        }

        var dashboardBaseUrl = config.GetValue("DashboardBaseUrl", "http://localhost:5173")!;

        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var conn = new AgentConnection(ws, new SemaphoreSlim(1, 1));
        hub.Connections[clientId] = conn;

        var state = stateStore.GetOrCreate(clientId);
        state.IsConnected = true;
        state.ConnectedAt = DateTimeOffset.UtcNow;

        Console.WriteLine($"[Server] Agent connected: {clientId}");

        string accountIdRaw = context.Request.Query["accountId"].ToString();
        string agentAccountId = !string.IsNullOrEmpty(accountIdRaw) ? accountIdRaw : clientId;
        state.AccountId = agentAccountId;

        // One password per account — shared across all agents of the same account
        var password = hub.AccountPasswords.GetOrAdd(agentAccountId, _ => TunnelHub.GeneratePassword());
        var authPayload = new SessionAuthPayload
        {
            DashboardUrl = $"{dashboardBaseUrl}/dashboard?token={password}",
            Token = password,
            Password = password,
        };
        await hub.SendToAgentAsync(clientId, ControlFrameBuilder.Build(ControlFrame.SessionAuth, authPayload));
        await hub.SyncRulesToAgentAsync(clientId);

        var receiveBuffer = new byte[64 * 1024];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                // WebSocket can fragment one logical message across multiple frames — collect them all
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
                if (packet.Length == 0) continue;

                // Heartbeat ping: single byte 0x00, no header
                if (packet.Length == 1 && packet[0] == 0x00) continue;

                // Every real packet has at least 16 bytes requestId + 1 byte type
                if (packet.Length < 17) continue;

                var idBytes = new byte[16];
                Array.Copy(packet, 0, idBytes, 0, 16);
                var id = new Guid(idBytes);
                byte packetType = packet[16];

                // Control frames use Guid.Empty as requestId — they're not tied to any HTTP request
                if (packetType == ControlFrame.LogEvent && id == Guid.Empty)
                {
                    if (packet.Length < 21) continue;
                    int jsonLen = BitConverter.ToInt32(packet, 17);
                    if (jsonLen <= 0 || packet.Length < 21 + jsonLen) continue;

                    string json = Encoding.UTF8.GetString(packet, 21, jsonLen);
                    var entry = ControlFrameBuilder.Deserialize<RequestLogEntry>(json);
                    if (entry is not null)
                    {
                        state.AddLogEntry(entry);
                        await sseMgr.BroadcastAsync(clientId, json);
                    }
                    continue;
                }

                // 0x01: response header from agent
                // Layout: [17..21) statusCode | [21..25) contentTypeLen | [25..25+n) contentType | [25+n..29+n) headersJsonLen | [29+n..) headersJson
                if (packetType == 0x01)
                {
                    int statusCode = BitConverter.ToInt32(packet, 17);
                    int typeLen = BitConverter.ToInt32(packet, 21);
                    string contentType = Encoding.UTF8.GetString(packet, 25, typeLen);
                    int headersJsonLen = BitConverter.ToInt32(packet, 25 + typeLen);
                    string headersJson = Encoding.UTF8.GetString(packet, 29 + typeLen, headersJsonLen);
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(headersJson)
                                  ?? new Dictionary<string, string[]>();

                    // Channel bridges the WebSocket receive loop and the HTTP response writer
                    var channel = Channel.CreateUnbounded<byte[]>();
                    hub.ActiveChannels[id] = channel;

                    // Unblocks the HTTP handler that's waiting on tcs.Task for this requestId
                    if (hub.PendingRequests.TryRemove(id, out var tcs))
                        tcs.SetResult(new AgentResponse(contentType, statusCode, channel.Reader, headers));
                }
                // 0x02: body chunk — everything after the 17-byte header is raw body bytes
                else if (packetType == 0x02)
                {
                    if (hub.ActiveChannels.TryGetValue(id, out var channel))
                    {
                        var chunk = new byte[packet.Length - 17];
                        Array.Copy(packet, 17, chunk, 0, chunk.Length);
                        await channel.Writer.WriteAsync(chunk);
                    }
                }
                // 0x03: EOF — signals ReadAllAsync() on the reader side to stop
                else if (packetType == 0x03)
                {
                    if (hub.ActiveChannels.TryRemove(id, out var channel))
                        channel.Writer.Complete();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Error ({clientId}): {ex.Message}");
        }
        finally
        {
            hub.Connections.TryRemove(clientId, out _);
            state.IsConnected = false;
            Console.WriteLine($"[Server] Agent disconnected: {clientId}");
        }

        return Results.Empty;
    }
}
