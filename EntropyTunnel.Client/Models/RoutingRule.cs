namespace EntropyTunnel.Client.Models;

/// <summary>
/// Routes matched paths to a specific local base URL instead of the default port.
/// Example: /api/* → http://localhost:5000, everything else → http://localhost:5173.
/// </summary>
public sealed record RoutingRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string PathPattern { get; init; } = "**";
    public string TargetBaseUrl { get; init; } = "http://localhost:3000";
    public bool IsEnabled { get; init; } = true;

    /// <summary>Lower number = matched first (higher priority).</summary>
    public int Priority { get; init; }
}
