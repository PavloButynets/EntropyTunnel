using System.Text;
using EntropyTunnel.Client.State;
using EntropyTunnel.Client.Pipeline;
using EntropyTunnel.Core.Models;

namespace EntropyTunnel.Client.Stages;

/// <summary>
/// Stage 2 — Chaos Engine.
/// Injects latency (uniform ± jitter) and probabilistic errors for matching requests.
/// If an error is injected IsHandled is set, skipping the real local call.
/// </summary>
public sealed class ChaosEngine : IPipelineStage
{
    private readonly RuleStore _store;
    private readonly ILogger<ChaosEngine> _logger;

    public ChaosEngine(RuleStore store, ILogger<ChaosEngine> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task ExecuteAsync(TunnelContext context, Func<Task> next, CancellationToken ct = default)
    {
        var rule = _store.GetChaosRules()
            .FirstOrDefault(r => r.IsEnabled && Matches(context, r.PathPattern, r.Method));

        if (rule is null)
        {
            await next();
            return;
        }

        context.AppliedChaosRule = rule.Name;

        // Latency: uniform random in [latencyMs - jitterMs, latencyMs + jitterMs]
        if (rule.LatencyMs > 0)
        {
            int jitter = rule.JitterMs > 0
                ? Random.Shared.Next(-rule.JitterMs, rule.JitterMs + 1)
                : 0;
            int delayMs = Math.Max(0, rule.LatencyMs + jitter);

            _logger.LogInformation("[CHAOS] '{Rule}': injecting {Delay}ms on {Path}",
                rule.Name, delayMs, context.Path);

            await Task.Delay(delayMs, ct);
        }

        // Error: independent per-request probability
        if (rule.ErrorRate > 0 && Random.Shared.NextDouble() < rule.ErrorRate)
        {
            _logger.LogWarning("[CHAOS] '{Rule}': injecting {Code} on {Path} (rate {Rate:P0})",
                rule.Name, rule.ErrorStatusCode, context.Path, rule.ErrorRate);

            context.StatusCode = rule.ErrorStatusCode;
            context.ContentType = "text/plain";
            context.ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(rule.ErrorBody));
            context.IsHandled = true;
            return;
        }

        await next();
    }

    private static bool Matches(TunnelContext ctx, string pathPattern, string? method) =>
        (method is null || string.Equals(ctx.Method, method, StringComparison.OrdinalIgnoreCase))
        && PathMatcher.Matches(ctx.Path, pathPattern);
}
