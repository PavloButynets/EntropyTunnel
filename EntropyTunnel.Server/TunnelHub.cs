using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using EntropyTunnel.Core;
using EntropyTunnel.Core.Payloads;
using EntropyTunnel.Server.State;

namespace EntropyTunnel.Server;

public sealed class TunnelHub
{
    public readonly ConcurrentDictionary<string, AgentConnection> Connections = new(StringComparer.OrdinalIgnoreCase);
    public readonly ConcurrentDictionary<Guid, TaskCompletionSource<AgentResponse>> PendingRequests = new();
    public readonly ConcurrentDictionary<Guid, Channel<byte[]>> ActiveChannels = new();

    private readonly AgentStateStore _stateStore;

    public TunnelHub(AgentStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task SendToAgentAsync(string clientId, byte[] packet)
    {
        if (!Connections.TryGetValue(clientId, out var conn)
            || conn.Socket.State != WebSocketState.Open) return;

        await conn.Lock.WaitAsync();
        try
        {
            if (conn.Socket.State == WebSocketState.Open)
                await conn.Socket.SendAsync(
                    new ArraySegment<byte>(packet),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    CancellationToken.None);
        }
        finally { conn.Lock.Release(); }
    }

    public async Task SyncRulesToAgentAsync(string clientId)
    {
        var payload = await _stateStore.GetSyncPayloadAsync(clientId);
        await SendToAgentAsync(clientId, ControlFrameBuilder.Build(ControlFrame.SyncRules, payload));
    }

    public static string GeneratePassword()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }
}

public record AgentConnection(WebSocket Socket, SemaphoreSlim Lock);
public record AgentResponse(string ContentType, int StatusCode, ChannelReader<byte[]> BodyReader, Dictionary<string, string[]> Headers);
