using EntropyTunnel.Client.Configuration;
using EntropyTunnel.Client.Dashboard;
using EntropyTunnel.Client.Pipeline;

namespace EntropyTunnel.Client.Stages;

/// <summary>
/// Stage 3 — Request Router.
/// Resolves context.TargetUrl from runtime routing rules.
/// Falls back to http://localhost:{defaultPort} if no rule matches.
/// Does not short-circuit — always calls next().
/// </summary>
public sealed class RequestRouter : IPipelineStage
{
    private readonly RuleStore _store;
    private readonly TunnelSettings _settings;
    private readonly ILogger<RequestRouter> _logger;

    public RequestRouter(RuleStore store, TunnelSettings settings, ILogger<RequestRouter> logger)
    {
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    public Task ExecuteAsync(TunnelContext context, Func<Task> next, CancellationToken ct = default)
    {
        // Rules are already ordered by Priority ascending in GetRoutingRules().
        var rule = _store.GetRoutingRules()
            .FirstOrDefault(r => r.IsEnabled && PathMatcher.Matches(context.Path, r.PathPattern));

        context.TargetUrl = rule is not null
            ? $"{rule.TargetBaseUrl.TrimEnd('/')}{context.Path}"
            : $"http://localhost:{_settings.LocalPort}{context.Path}";

        if (rule is not null)
            _logger.LogDebug("[ROUTE] {Path} → {TargetUrl} via rule '{Rule}'",
                context.Path, context.TargetUrl, rule.Name);

        return next();
    }
}
