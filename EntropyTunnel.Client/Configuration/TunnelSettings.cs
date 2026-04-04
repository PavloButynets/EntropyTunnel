namespace EntropyTunnel.Client.Configuration;

public sealed class TunnelSettings
{
    public string ServerUrl { get; set; } = "wss://entropy-tunnel.xyz/tunnel";
    public string PublicDomain { get; set; } = "entropy-tunnel.xyz";
    public string ClientId { get; set; } = "default";
    public int LocalPort { get; set; } = 5173;
    public string AccountId { get; set; } = string.Empty;
}
