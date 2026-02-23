namespace EntropyTunnel.Client.Pipeline;

/// <summary>
/// Middleware-style pipeline stage. Call <paramref name="next"/> to pass control to
/// the following stage, or set <see cref="TunnelContext.IsHandled"/> = true to short-circuit.
/// </summary>
public interface IPipelineStage
{
    Task ExecuteAsync(TunnelContext context, Func<Task> next, CancellationToken ct = default);
}
