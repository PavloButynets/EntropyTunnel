namespace EntropyTunnel.Client.Models;

/// <summary>Immutable snapshot of a completed request - stored in the Inspector ring buffer.</summary>
public sealed record RequestLogEntry
{
    // ── Basic fields ──────────────────────────────────────────────────────────
    public Guid RequestId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public long DurationMs { get; init; }
    public string? AppliedChaosRule { get; init; }
    public string? AppliedMockRule { get; init; }
    public string? ResolvedTargetUrl { get; init; }

    // - Full request detail - for Inspector UI
    /// <summary>Headers forwarded from the tunnel server to this agent (hop-by-hop already stripped).</summary>
    public Dictionary<string, string>? RequestHeaders { get; init; }

    /// <summary>UTF-8 preview of the first 4 KB of the request body. Null when no body was present.</summary>
    public string? RequestBodyPreview { get; init; }

    /// <summary>Total request body size in bytes (from Content-Length or stream length).</summary>
    public long? RequestContentLength { get; init; }

    /// <summary>Response headers from the local service, comma-joined for display.</summary>
    public Dictionary<string, string>? ResponseHeaders { get; init; }
}
