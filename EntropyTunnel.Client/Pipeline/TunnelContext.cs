using System.Diagnostics;

namespace EntropyTunnel.Client.Pipeline;

/// <summary>
/// Carries all state for a single tunnelled request as it travels through the pipeline.
/// Analogous to HttpContext in ASP.NET Core middleware.
/// </summary>
public sealed class TunnelContext
{
    // Request
    public required Guid RequestId { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public Dictionary<string, string>? RequestHeaders { get; init; }
    public Stream? RequestBody { get; init; }


    // - Mutable pipeline state
    /// <summary>Set by RequestRouter. Read by LocalForwarder.</summary>
    public string TargetUrl { get; set; } = string.Empty;

    // ── Response (filled by whichever stage handles the request) ──────────────
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "application/octet-stream";
    public Stream? ResponseStream { get; set; }
    // Values are string[] so that multi-value headers (Set-Cookie, WWW-Authenticate, Link…)
    // are preserved as separate header lines
    public Dictionary<string, string[]>? ResponseHeaders { get; set; }


    /// <summary>
    /// When true, remaining stages are skipped.
    /// Set by MockEngine (mock hit) or ChaosEngine (error injection).
    /// </summary>
    public bool IsHandled { get; set; }

    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
    public string? AppliedChaosRule { get; set; }
    public string? AppliedMockRule { get; set; }
}
