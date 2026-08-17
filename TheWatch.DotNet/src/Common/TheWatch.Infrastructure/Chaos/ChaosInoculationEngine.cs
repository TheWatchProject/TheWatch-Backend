using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Chaos;

/// <summary>
/// Injects simulated network latency, transient database failures, and packet drops
/// to inoculate and verify platform self-healing resilience in staging and pre-prod.
/// </summary>
public class ChaosInoculationEngine
{
    private readonly ILogger<ChaosInoculationEngine> _logger;
    private static readonly Random s_random = new();

    /// <summary>
    /// Initializes a new instance of <see cref="ChaosInoculationEngine"/>.
    /// </summary>
    /// <param name="logger">Logger service.</param>
    public ChaosInoculationEngine(ILogger<ChaosInoculationEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Conditionally injects artificial latency or transient failure based on chaos probability.
    /// </summary>
    /// <param name="failureRate">Probability of failure (0.0 to 1.0).</param>
    /// <param name="maxLatencyMs">Maximum injected latency in milliseconds.</param>
    public async Task InoculateChaosAsync(double failureRate = 0.05, int maxLatencyMs = 250)
    {
        if (s_random.NextDouble() < failureRate)
        {
            _logger.LogWarning("CHAOS INJECTION: Simulating transient downstream network failure.");
            throw new InvalidOperationException("Simulated Chaos Transient Network Exception");
        }

        if (maxLatencyMs > 0)
        {
            var delay = s_random.Next(50, maxLatencyMs);
            await Task.Delay(delay);
        }
    }
}
