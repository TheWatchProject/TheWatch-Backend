using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Resilience;

public interface IPipelineProvider
{
    Task<T> ExecuteAsync<T>(string tenantId, Func<CancellationToken, Task<T>> action, CancellationToken ct = default);
}

public class PollyPipelineProvider : IPipelineProvider
{
    private readonly ILogger<PollyPipelineProvider> _logger;
    private readonly ConcurrentDictionary<string, int> _failureCounts = new();

    public PollyPipelineProvider(ILogger<PollyPipelineProvider> logger)
    {
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(string tenantId, Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await action(ct);
                _failureCounts.TryRemove(tenantId, out _);
                return result;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var currentFailures = _failureCounts.AddOrUpdate(tenantId, 1, (_, count) => count + 1);
                _logger.LogWarning(ex, "Resilience execution attempt {Attempt}/{Max} failed for tenant {TenantId}. Consec failures: {Failures}",
                    attempt, maxRetries, tenantId, currentFailures);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), ct);
            }
        }

        return await action(ct);
    }
}
