namespace EntropyTunnel.Client.Configuration;

public sealed class TunnelSettings
{
    public string ServerUrl { get; set; } = "ws://localhost:8080/tunnel";
    public string PublicDomain { get; set; } = "localhost:8080";
    public string ClientId { get; set; } = "default";
    public int LocalPort { get; set; } = 5173;
    public int DashboardPort { get; set; } = 4040;
}
