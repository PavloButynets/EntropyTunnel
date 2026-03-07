namespace EntropyTunnel.Core.Payloads;

/// <summary>
/// Payload for the 0x22 SessionAuth control frame (Server → Client).
/// Sent by the server immediately after a successful agent connection to hand the
/// agent its remote dashboard URL and a short-lived session token.
/// The client prints this as an ngrok-style connection banner.
/// </summary>
public sealed record SessionAuthPayload
{
    /// <summary>Full HTTPS URL of the remote dashboard for this agent.</summary>
    public string DashboardUrl { get; init; } = string.Empty;

    /// <summary>Short-lived bearer token scoped to this agent session.</summary>
    public string Token { get; init; } = string.Empty;
}
