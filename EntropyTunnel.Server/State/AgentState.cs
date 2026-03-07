using System.Collections.Concurrent;
using EntropyTunnel.Core.Models;

namespace EntropyTunnel.Server.State;

/// <summary>
/// All mutable state owned by a single connected agent.
/// Thread-safe by design: rule collections use ConcurrentDictionary;
/// the request log uses a lock-protected circular buffer.
/// </summary>
public sealed class AgentState
{
    // Rules 

    public ConcurrentDictionary<Guid, ChaosRule> ChaosRules { get; } = new();
    public ConcurrentDictionary<Guid, MockRule> MockRules { get; } = new();
    public ConcurrentDictionary<Guid, RoutingRule> RoutingRules { get; } = new();

    // Circular log buffer - max 1 000 entries

    private const int MaxLogEntries = 1_000;
    private readonly RequestLogEntry[] _ring = new RequestLogEntry[MaxLogEntries];
    private int _writePos; // index of next write slot
    private int _count;    // number of valid entries currently in the buffer
    private readonly object _ringLock = new();

    public void AddLogEntry(RequestLogEntry entry)
    {
        lock (_ringLock)
        {
            _ring[_writePos] = entry;
            _writePos = (_writePos + 1) % MaxLogEntries;
            if (_count < MaxLogEntries) _count++;
        }
    }

    /// <summary>Returns up to 1 000 entries, newest first.</summary>
    public IReadOnlyList<RequestLogEntry> GetLog()
    {
        lock (_ringLock)
        {
            if (_count == 0) return [];

            var result = new RequestLogEntry[_count];
            for (int i = 0; i < _count; i++)
            {
                // Walk backwards from the last-written slot
                int idx = ((_writePos - 1 - i) % MaxLogEntries + MaxLogEntries) % MaxLogEntries;
                result[i] = _ring[idx];
            }
            return result;
        }
    }

    public void ClearLog()
    {
        lock (_ringLock)
        {
            _writePos = 0;
            _count = 0;
        }
    }

    // Connection state

    /// <summary>True while the agent's WebSocket is open.</summary>
    public volatile bool IsConnected;

    public string PublicUrl { get; set; } = string.Empty;
    public DateTimeOffset? ConnectedAt { get; set; }
}
