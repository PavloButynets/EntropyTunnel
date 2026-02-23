using System.Collections.Concurrent;
using EntropyTunnel.Client.Models;

namespace EntropyTunnel.Client.Dashboard;

/// <summary>
/// Thread-safe, in-process store for all runtime-mutable rules and the request log.
///
/// Design: ConcurrentDictionary&lt;Guid, T&gt; per rule type gives O(1) add/remove/lookup
/// without any external locks.  Iteration snapshots (.Values) are safe and consistent
/// because ConcurrentDictionary never yields an inconsistent view.
///
/// Request log: bounded ConcurrentQueue — at most MaxLogEntries entries (FIFO eviction).
/// </summary>
public sealed class RuleStore
{
    private readonly ConcurrentDictionary<Guid, ChaosRule> _chaos = new();
    private readonly ConcurrentDictionary<Guid, MockRule> _mocks = new();
    private readonly ConcurrentDictionary<Guid, RoutingRule> _routing = new();

    private readonly ConcurrentQueue<RequestLogEntry> _log = new();
    private const int MaxLogEntries = 200;

    // ── Chaos Rules ───────────────────────────────────────────────────────────

    /// <summary>Snapshot ordered by name. Safe for concurrent reads.</summary>
    public IEnumerable<ChaosRule> GetChaosRules() =>
        _chaos.Values.OrderBy(r => r.Name);

    public void AddChaosRule(ChaosRule rule) =>
        _chaos[rule.Id] = rule;

    public bool UpdateChaosRule(ChaosRule rule)
    {
        if (!_chaos.ContainsKey(rule.Id)) return false;
        _chaos[rule.Id] = rule;
        return true;
    }

    public bool RemoveChaosRule(Guid id) =>
        _chaos.TryRemove(id, out _);

    /// <summary>Atomically flips IsEnabled and returns the updated rule, or null if not found.</summary>
    public ChaosRule? ToggleChaosRule(Guid id)
    {
        if (!_chaos.TryGetValue(id, out var existing)) return null;
        var toggled = existing with { IsEnabled = !existing.IsEnabled };
        _chaos[id] = toggled;
        return toggled;
    }

    // ── Mock Rules ────────────────────────────────────────────────────────────

    public IEnumerable<MockRule> GetMockRules() =>
        _mocks.Values.OrderBy(r => r.Name);

    public void AddMockRule(MockRule rule) =>
        _mocks[rule.Id] = rule;

    public bool UpdateMockRule(MockRule rule)
    {
        if (!_mocks.ContainsKey(rule.Id)) return false;
        _mocks[rule.Id] = rule;
        return true;
    }

    public bool RemoveMockRule(Guid id) =>
        _mocks.TryRemove(id, out _);

    // ── Routing Rules ─────────────────────────────────────────────────────────

    /// <summary>Snapshot ordered by Priority ascending (lowest = matched first).</summary>
    public IEnumerable<RoutingRule> GetRoutingRules() =>
        _routing.Values.OrderBy(r => r.Priority);

    public void AddRoutingRule(RoutingRule rule) =>
        _routing[rule.Id] = rule;

    public bool UpdateRoutingRule(RoutingRule rule)
    {
        if (!_routing.ContainsKey(rule.Id)) return false;
        _routing[rule.Id] = rule;
        return true;
    }

    public bool RemoveRoutingRule(Guid id) =>
        _routing.TryRemove(id, out _);

    // ── Request Log ───────────────────────────────────────────────────────────

    /// <summary>Returns up to MaxLogEntries entries, newest first.</summary>
    public IEnumerable<RequestLogEntry> GetRequestLog() =>
        _log.Reverse();

    public void LogRequest(RequestLogEntry entry)
    {
        _log.Enqueue(entry);
        // Trim oldest entries beyond the cap
        while (_log.Count > MaxLogEntries)
            _log.TryDequeue(out _);
    }

    public void ClearRequestLog()
    {
        while (_log.TryDequeue(out _)) { }
    }
}
