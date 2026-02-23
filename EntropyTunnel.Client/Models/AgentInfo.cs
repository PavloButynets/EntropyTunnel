namespace EntropyTunnel.Client.Models;

/// <summary>
/// Represents a running EntropyTunnel agent instance.
/// The primary dashboard collects these from secondary agents via /api/agents/register.
/// </summary>
public sealed record AgentInfo
{
    public string ClientId { get; init; } = string.Empty;
    public int LocalPort { get; init; }

    /// <summary>Full base URL of this agent's own dashboard/API, e.g. http://localhost:4041</summary>
    public string ApiUrl { get; init; } = string.Empty;

    /// <summary>True for the agent that owns the primary dashboard (port 4040).</summary>
    public bool IsPrimary { get; init; }

    public bool IsConnected { get; init; }
    public string PublicUrl { get; init; } = string.Empty;
    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.UtcNow;
}
