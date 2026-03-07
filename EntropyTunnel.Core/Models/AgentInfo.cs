namespace EntropyTunnel.Core.Models;

/// <summary>
/// Snapshot of a connected (or previously connected) agent's state.
/// Returned by GET /api/agents on the server.
/// </summary>
public sealed record AgentInfo
{
    public string ClientId { get; init; } = string.Empty;
    public bool IsConnected { get; init; }
    public string PublicUrl { get; init; } = string.Empty;
    public DateTimeOffset? ConnectedAt { get; init; }
}
