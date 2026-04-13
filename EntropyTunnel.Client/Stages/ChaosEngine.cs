using System.Text;
using System.Collections.Concurrent;
using EntropyTunnel.Client.State;
using EntropyTunnel.Client.Pipeline;
using EntropyTunnel.Core;
using EntropyTunnel.Core.Models;

namespace EntropyTunnel.Client.Stages;

/// <summary>
/// Stage 2 - Chaos Engine.
/// Applies per-path latency injection and probabilistic error injection.
/// Supports multiple distribution types for realistic failure simulation.
/// If an error is injected IsHandled is set, skipping the real local call.
/// If only latency is injected, next() is still called (the request proceeds normally but slowly).
/// </summary>
public sealed class ChaosEngine : IPipelineStage
{
    private readonly RuleStore _store;
    private readonly ILogger<ChaosEngine> _logger;

    // Poisson error state per rule ID
    private readonly ConcurrentDictionary<Guid, ErrorInjectionState> _errorStates = new();

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

        // 1. Latency injection using distribution sampler
        int delayMs = DistributionSampler.SampleLatency(rule);
        if (delayMs > 0)
        {
            _logger.LogInformation("[CHAOS] '{Rule}': injecting {Delay}ms latency on {Path}",
                rule.Name, delayMs, context.Path);

            await Task.Delay(delayMs, ct);
        }

        // 2. Error injection using distribution sampler
        var errorState = _errorStates.GetOrAdd(rule.Id, _ => new ErrorInjectionState());

        if (DistributionSampler.ShouldInjectError(rule, errorState))
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
