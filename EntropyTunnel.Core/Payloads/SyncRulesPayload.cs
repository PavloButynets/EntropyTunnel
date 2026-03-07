using EntropyTunnel.Core.Models;

namespace EntropyTunnel.Core.Payloads;

/// <summary>
/// Payload for the 0x20 SyncRules control frame (Server → Client).
/// Carries the full set of active rules for one agent so the client can atomically
/// replace its local in-memory configuration.
/// </summary>
public sealed record SyncRulesPayload
{
    public List<ChaosRule>   ChaosRules   { get; init; } = [];
    public List<MockRule>    MockRules    { get; init; } = [];
    public List<RoutingRule> RoutingRules { get; init; } = [];
}
