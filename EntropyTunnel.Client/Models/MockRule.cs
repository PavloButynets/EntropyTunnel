namespace EntropyTunnel.Client.Models;

/// <summary>
/// A rule that short-circuits the pipeline and returns a canned response
/// without forwarding to the local service.
/// </summary>
public sealed record MockRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string PathPattern { get; init; } = string.Empty;

    /// <summary>HTTP method filter. null = any method.</summary>
    public string? Method { get; init; }

    public bool IsEnabled { get; init; } = true;
    public int StatusCode { get; init; } = 200;
    public string ContentType { get; init; } = "application/json";
    public string ResponseBody { get; init; } = "{}";
}
