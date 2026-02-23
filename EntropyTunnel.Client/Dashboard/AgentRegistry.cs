using System.Collections.Concurrent;
using EntropyTunnel.Client.Models;

namespace EntropyTunnel.Client.Dashboard;

/// <summary>
/// Thread-safe registry of all secondary agents that have registered with this primary dashboard.
/// Only meaningfully populated on the primary instance; secondary registries stay empty.
/// </summary>
public sealed class AgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentInfo> _agents =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Snapshot of all registered secondary agents, ordered by ClientId.</summary>
    public IReadOnlyList<AgentInfo> GetAll() =>
        _agents.Values.OrderBy(a => a.ClientId).ToList();

    /// <summary>Upsert: adds or replaces the agent entry. Updates LastSeen to now.</summary>
    public void Register(AgentInfo agent) =>
        _agents[agent.ClientId] = agent with { LastSeen = DateTimeOffset.UtcNow };

    /// <summary>Returns true if the agent was found and removed.</summary>
    public bool Unregister(string clientId) =>
        _agents.TryRemove(clientId, out _);

    /// <summary>Remove agents that haven't re-registered in the last <paramref name="maxAge"/>.</summary>
    public void PruneStale(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (var (key, agent) in _agents)
            if (agent.LastSeen < cutoff)
                _agents.TryRemove(key, out _);
    }
}
