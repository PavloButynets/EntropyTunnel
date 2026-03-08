namespace EntropyTunnel.Client.Configuration;

public sealed class TunnelSettings
{
    public string ServerUrl { get; set; } = "ws://130.61.202.172:8080/tunnel";
    public string PublicDomain { get; set; } = "130.61.202.172.nip.io:8080";
    public string ClientId { get; set; } = "default";
    public int LocalPort { get; set; } = 5173;
    public string AccountId { get; set; } = string.Empty;
}
