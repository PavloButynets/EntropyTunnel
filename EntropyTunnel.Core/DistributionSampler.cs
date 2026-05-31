using EntropyTunnel.Core.Models;

namespace EntropyTunnel.Core;

/// <summary>
/// Samples latency and error occurrence from various statistical distributions.
/// All latency results are clamped to [0, 30000] ms range.
/// </summary>
public static class DistributionSampler
{
    private const int MaxLatencyMs = 30000;

    /// <summary>
    /// Sample latency in ms according to the rule's LatencyDistribution.
    /// Returns 0 if LatencyMs is 0.
    /// </summary>
    public static int SampleLatency(ChaosRule rule)
    {
        if (rule.LatencyMs <= 0) return 0;

        var latency = rule.LatencyDistribution switch
        {
            LatencyDistribution.Uniform => SampleUniform(rule.LatencyMs, rule.JitterMs),
            LatencyDistribution.Gaussian => SampleGaussian(rule.LatencyMs, rule.JitterMs),
            _ => rule.LatencyMs,
        };

        return Math.Max(0, Math.Min((int)latency, MaxLatencyMs));
    }

    /// <summary>
    /// Determine if an error should be injected according to the rule's ErrorDistribution.
    /// </summary>
    public static bool ShouldInjectError(ChaosRule rule, ErrorInjectionState state)
    {
        if (rule.ErrorRate <= 0) return false;

        return rule.ErrorDistribution switch
        {
            ErrorDistribution.Random => Random.Shared.NextDouble() < rule.ErrorRate,
            ErrorDistribution.Poisson => ShouldInjectErrorPoisson(rule, state),
            _ => false,
        };
    }

    // Latency Samplers

    private static int SampleUniform(int mean, int jitter)
    {
        if (jitter <= 0) return mean;
        int offset = Random.Shared.Next(-jitter, jitter + 1);
        return mean + offset;
    }

    /// <summary>
    /// Box-Muller transform for Gaussian sampling.
    /// mean = LatencyMs, stdDev = JitterMs
    /// </summary>
    private static double SampleGaussian(int mean, int stdDev)
    {
        if (stdDev <= 0) return mean;

        double u1 = 1.0 - Random.Shared.NextDouble(); // (0, 1]
        double u2 = 1.0 - Random.Shared.NextDouble(); // (0, 1]

        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }

    // Error Samplers

    /// <summary>
    /// Poisson error injection: tracks burst state to generate correlated errors.
    /// </summary>
    private static bool ShouldInjectErrorPoisson(ChaosRule rule, ErrorInjectionState state)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (state)
        {
            if (state.BurstEndTimeMs > nowMs)
            {
                // Still in a burst - inject error with probability = ErrorRate
                return Random.Shared.NextDouble() < rule.ErrorRate;
            }

            // Not in burst; decide if we start a new one
            if (Random.Shared.NextDouble() < rule.PoissonLambda)
            {
                // Start a burst
                state.BurstEndTimeMs = nowMs + rule.PoissonBurstDurationMs;
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// Tracks state for Poisson error distribution (burst start/end times per rule).
/// </summary>
public sealed class ErrorInjectionState
{
    /// <summary>Unix timestamp (ms) when the current error burst should end. 0 = no burst.</summary>
    public long BurstEndTimeMs { get; set; }
}
