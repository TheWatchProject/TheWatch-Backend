using Microsoft.Extensions.Logging;
using Polly;
using TheWatch.ServiceDefaults.Resilience;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Preserves the existing Infrastructure API over the generated resilience policy model.
/// </summary>
public sealed class ResiliencePolicyService
{
    private readonly ILogger<ResiliencePolicyService> _logger;

    public ResiliencePolicyService(ILogger<ResiliencePolicyService> logger) => _logger = logger;

    public ResiliencePipeline GetStandardRetryPolicy() =>
        GeneratedResiliencePipelineFactory.Create(
            ResiliencePolicyOptions.Standard with
            {
                EnableCircuitBreaker = false,
                EnableTimeout = false,
                Retry = ResiliencePolicyOptions.Standard.Retry with
                {
                    BaseDelay = TimeSpan.FromSeconds(1),
                    UseJitter = false,
                },
            },
            _logger);

    public ResiliencePipeline GetCircuitBreakerPolicy() =>
        GeneratedResiliencePipelineFactory.Create(
            ResiliencePolicyOptions.Standard with
            {
                EnableRetry = false,
                EnableTimeout = false,
            },
            _logger);

    public ResiliencePipeline GetTimeoutPolicy(TimeSpan timeout) =>
        GeneratedResiliencePipelineFactory.Create(
            ResiliencePolicyOptions.Standard with
            {
                Timeout = timeout,
                EnableRetry = false,
                EnableCircuitBreaker = false,
            },
            _logger);

    public ResiliencePipeline GetCombinedPolicy(TimeSpan timeout) =>
        GeneratedResiliencePipelineFactory.Create(
            ResiliencePolicyOptions.Standard with
            {
                Timeout = timeout,
                Retry = ResiliencePolicyOptions.Standard.Retry with
                {
                    BaseDelay = TimeSpan.FromSeconds(1),
                    UseJitter = false,
                },
            },
            _logger);

    public ResiliencePipeline GetCriticalServicePolicy() =>
        GeneratedResiliencePipelineFactory.CreateCritical(_logger);
}
