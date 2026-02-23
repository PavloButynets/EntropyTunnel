using System.Text;
using EntropyTunnel.Client.Dashboard;
using EntropyTunnel.Client.Pipeline;

namespace EntropyTunnel.Client.Stages;

/// <summary>
/// Stage 1 - Mock Engine.
/// Checks the runtime RuleStore for a matching MockRule.
/// On a hit: fills the context with the canned response and sets IsHandled = true,
/// short-circuiting all remaining stages (no local service call is made).
/// </summary>
public sealed class MockEngine : IPipelineStage
{
    private readonly RuleStore _store;
    private readonly ILogger<MockEngine> _logger;

    public MockEngine(RuleStore store, ILogger<MockEngine> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task ExecuteAsync(TunnelContext context, Func<Task> next, CancellationToken ct = default)
    {
        var rule = _store.GetMockRules()
            .FirstOrDefault(r => r.IsEnabled && Matches(context, r.PathPattern, r.Method));

        if (rule is null)
        {
            await next();
            return;
        }

        _logger.LogInformation("[MOCK] {Method} {Path} matched rule '{Name}' → {Status}",
            context.Method, context.Path, rule.Name, rule.StatusCode);

        context.StatusCode = rule.StatusCode;
        context.ContentType = rule.ContentType;
        context.ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(rule.ResponseBody));
        context.AppliedMockRule = rule.Name;
        context.IsHandled = true;
        // Do NOT call next() — short-circuit complete.
    }

    private static bool Matches(TunnelContext ctx, string pathPattern, string? method) =>
        (method is null || string.Equals(ctx.Method, method, StringComparison.OrdinalIgnoreCase))
        && PathMatcher.Matches(ctx.Path, pathPattern);
}
