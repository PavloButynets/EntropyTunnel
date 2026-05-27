namespace EntropyTunnel.Server.Data;

public sealed class LogRow
{
    public Guid RequestId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Data { get; set; } = string.Empty;
}
