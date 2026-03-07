using System.Collections.Concurrent;

namespace EntropyTunnel.Server.State;

/// <summary>
/// Multi-tenant store that holds an <see cref="AgentState"/> for every agent that has
/// ever connected.  Keyed by clientId (case-insensitive).
///
/// Agents are never evicted so that accumulated rules and logs survive reconnects.
/// </summary>
public sealed class AgentStateStore
{
    private readonly ConcurrentDictionary<string, AgentState> _agents =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the existing state for <paramref name="clientId"/> or creates a new one.</summary>
    public AgentState GetOrCreate(string clientId) =>
        _agents.GetOrAdd(clientId, _ => new AgentState());

    /// <summary>Returns the existing state or null if the agent has never connected.</summary>
    public AgentState? Get(string clientId) =>
        _agents.TryGetValue(clientId, out var s) ? s : null;

    /// <summary>Snapshot of all agent states ordered by clientId.</summary>
    public IEnumerable<(string ClientId, AgentState State)> GetAll() =>
        _agents.OrderBy(kvp => kvp.Key).Select(kvp => (kvp.Key, kvp.Value));
}
