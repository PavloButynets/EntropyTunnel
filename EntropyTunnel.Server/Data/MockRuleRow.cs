namespace EntropyTunnel.Server.Data;

public sealed class MockRuleRow
{
    public Guid Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}
