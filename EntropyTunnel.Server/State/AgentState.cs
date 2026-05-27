namespace EntropyTunnel.Server.State;

public sealed class AgentState
{
    public volatile bool IsConnected;
    public string AccountId { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
    public DateTimeOffset? ConnectedAt { get; set; }
}
