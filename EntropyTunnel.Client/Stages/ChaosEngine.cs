using System.Text;
using EntropyTunnel.Client.Dashboard;
using EntropyTunnel.Client.Pipeline;

namespace EntropyTunnel.Client.Stages;

/// <summary>
/// Stage 2 - Chaos Engine.
/// Applies per-path latency injection and probabilistic error injection.
/// If an error is injected IsHandled is set, skipping the real local call.
/// If only latency is injected, next() is still called (the request proceeds normally but slowly).
/// </summary>
public sealed class ChaosEngine : IPipelineStage
{
    private readonly RuleStore _store;
    private readonly ILogger<ChaosEngine> _logger;

    // Thread-local Random avoids lock contention on hot paths.
    [ThreadStatic]
    private static Random? _rng;
    private static Random Rng => _rng ??= new Random();

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

        // 1. Latency injection
        if (rule.LatencyMs > 0)
        {
            int jitter = rule.JitterMs > 0 ? Rng.Next(-rule.JitterMs, rule.JitterMs + 1) : 0;
            int delay = Math.Max(0, rule.LatencyMs + jitter);

            _logger.LogInformation("[CHAOS] '{Rule}': injecting {Delay}ms latency on {Path}",
                rule.Name, delay, context.Path);

            await Task.Delay(delay, ct);
        }

        // 2. Error injection
        if (rule.ErrorRate > 0 && Rng.NextDouble() < rule.ErrorRate)
        {
            _logger.LogWarning("[CHAOS] '{Rule}': injecting {Code} on {Path} (rate {Rate:P0})",
                rule.Name, rule.ErrorStatusCode, context.Path, rule.ErrorRate);

            context.StatusCode = rule.ErrorStatusCode;
            context.ContentType = "text/plain";
            context.ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(rule.ErrorBody));
            context.IsHandled = true;
            return; // Do NOT call next()
        }

        await next();
    }

    private static bool Matches(TunnelContext ctx, string pathPattern, string? method) =>
        (method is null || string.Equals(ctx.Method, method, StringComparison.OrdinalIgnoreCase))
        && PathMatcher.Matches(ctx.Path, pathPattern);
}
