namespace EntropyTunnel.Core.Models;

/// <summary>
/// Latency distribution type for chaos rules.
/// </summary>
public enum LatencyDistribution
{
    /// <summary>Uniform random [latencyMs - jitterMs, latencyMs + jitterMs]</summary>
    Uniform = 0,

    /// <summary>Gaussian with mean=latencyMs, stdDev=jitterMs (Box-Muller)</summary>
    Gaussian = 1,
}

/// <summary>
/// Error distribution type for chaos rules.
/// </summary>
public enum ErrorDistribution
{
    /// <summary>Random: each request has independent ErrorRate chance</summary>
    Random = 0,

    /// <summary>Poisson: errors cluster in bursts (correlated failures)</summary>
    Poisson = 1,
}

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
    public LatencyDistribution LatencyDistribution { get; init; } = LatencyDistribution.Uniform;

    // --- Error injection ---
    /// <summary>Probability 0.0–1.0 that this request gets a synthetic error response.</summary>
    public double ErrorRate { get; init; }
    public int ErrorStatusCode { get; init; } = 502;
    public string ErrorBody { get; init; } = "Chaos Engineering: Injected Error";
    public ErrorDistribution ErrorDistribution { get; init; } = ErrorDistribution.Random;

    // Poisson-specific (when ErrorDistribution == Poisson)
    /// <summary>Rate parameter for Poisson process. Typical range 0.01–0.5.</summary>
    public double PoissonLambda { get; init; } = 0.1;
    /// <summary>Duration in ms of a burst when Poisson triggers an error.</summary>
    public int PoissonBurstDurationMs { get; init; } = 2000;
}
