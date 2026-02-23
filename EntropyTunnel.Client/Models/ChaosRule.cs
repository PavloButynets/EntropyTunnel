namespace EntropyTunnel.Client.Models;

/// <summary>
/// A per-path chaos rule that injects latency and/or errors for matching requests.
/// Declared as a record so it supports immutable `with`-expression updates in RuleStore.
/// </summary>
public sealed record ChaosRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Glob-style path pattern: "**" = all, "/api/*" = prefix, "/api/checkout" = exact.
    /// </summary>
    public string PathPattern { get; init; } = "**";

    /// <summary>HTTP method filter. null = any method.</summary>
    public string? Method { get; init; }

    public bool IsEnabled { get; init; } = true;

    // --- Latency injection ---
    public int LatencyMs { get; init; }
    public int JitterMs { get; init; }

    // --- Error injection ---
    /// <summary>Probability 0.0–1.0 that this request gets a synthetic error response.</summary>
    public double ErrorRate { get; init; }
    public int ErrorStatusCode { get; init; } = 502;
    public string ErrorBody { get; init; } = "Chaos Engineering: Injected Error";
}
