namespace EntropyTunnel.Server.Data;

public sealed class RuleRow
{
    public Guid Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "chaos" | "mock" | "routing"
    public string Data { get; set; } = string.Empty;
}
