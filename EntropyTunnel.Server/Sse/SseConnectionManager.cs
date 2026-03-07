using System.Collections.Concurrent;
using System.Threading.Channels;

namespace EntropyTunnel.Server.Sse;

/// <summary>
/// Manages Server-Sent Event subscriptions per agent (clientId).
///
/// Each connected browser tab holds an active <see cref="Channel{T}"/> registered here.
/// When the server receives a 0x21 LogEvent frame from an agent it calls
/// <see cref="BroadcastAsync"/> to fan the serialized event out to all subscribers.
///
/// Channels are bounded (capacity 200, drop-oldest) so a slow browser tab cannot
/// block the server's WebSocket receive loop.
/// </summary>
public sealed class SseConnectionManager
{
    // clientId → { subscriptionId → channel }
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<string>>> _subs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Subscribes a new SSE connection for <paramref name="clientId"/>.
    /// Dispose the returned handle to unsubscribe and complete the channel.
    /// </summary>
    public (Channel<string> Channel, IDisposable Subscription) Subscribe(string clientId)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        var dict = _subs.GetOrAdd(clientId, _ => new ConcurrentDictionary<Guid, Channel<string>>());
        var id = Guid.NewGuid();
        dict[id] = channel;

        return (channel, new Subscription(() =>
        {
            dict.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }));
    }

    /// <summary>
    /// Writes <paramref name="jsonLine"/> to every active SSE subscriber for <paramref name="clientId"/>.
    /// Channels that are full drop the oldest entry (bounded, non-blocking).
    /// </summary>
    public async Task BroadcastAsync(string clientId, string jsonLine, CancellationToken ct = default)
    {
        if (!_subs.TryGetValue(clientId, out var dict)) return;

        foreach (var (_, ch) in dict)
        {
            try
            {
                await ch.Writer.WriteAsync(jsonLine, ct);
            }
            catch
            {
                // Channel completed or cancelled — subscriber already disconnected.
            }
        }
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
