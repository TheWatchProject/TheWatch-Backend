using System;
using Polly;

namespace TheWatch.ServiceDefaults.Resilience;

/// <summary>
/// Provides pre-configured enterprise Polly v8 resilience pipelines for HTTP clients and message consumers.
/// </summary>
/// <remarks>
/// Combines exponential jitter retries, circuit breaking, and aggressive timeouts to eliminate cascading outages.
/// </remarks>
public static class ResiliencePipelines
{
    /// <summary>
    /// Creates the standard multi-layered HTTP resilience pipeline.
    /// </summary>
    /// <returns>A configured <see cref="ResiliencePipeline"/> ready for execution.</returns>
    public static ResiliencePipeline CreateDefaultHttpPipeline()
        => GeneratedResiliencePipelineFactory.Create(
            ResiliencePolicyOptions.Standard with
            {
                Timeout = TimeSpan.FromSeconds(5),
                CircuitBreaker = ResiliencePolicyOptions.Standard.CircuitBreaker with
                {
                    BreakDuration = TimeSpan.FromSeconds(15),
                },
            });
}
