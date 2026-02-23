using EntropyTunnel.Client.Stages;

namespace EntropyTunnel.Client.Pipeline;

/// <summary>
/// Assembles and executes the ordered chain of pipeline stages.
///
/// Chain: MockEngine -> ChaosEngine -> RequestRouter -> LocalForwarder
/// </summary>
public sealed class RequestPipeline
{
    private readonly IReadOnlyList<IPipelineStage> _stages;

    public RequestPipeline(
        MockEngine mockEngine,
        ChaosEngine chaosEngine,
        RequestRouter requestRouter,
        LocalForwarder localForwarder)
    {
        _stages = [mockEngine, chaosEngine, requestRouter, localForwarder];
    }

    public Task ExecuteAsync(TunnelContext context, CancellationToken ct = default)
    {
        // Build the invocation chain right-to-left
        // Each wrapper checks IsHandled so a short-circuit skips all remaining stages.
        Func<Task> pipeline = () => Task.CompletedTask;

        for (int i = _stages.Count - 1; i >= 0; i--)
        {
            var stage = _stages[i];
            var next = pipeline;
            pipeline = () => context.IsHandled
                ? Task.CompletedTask
                : stage.ExecuteAsync(context, next, ct);
        }

        return pipeline();
    }
}
