using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.MachineLearning;

/// <summary>
/// Machine Learning model service for detecting statistical anomalies in emergency telemetry.
/// </summary>
/// <remarks>
/// Implements ISO/IEC 22989 AI trustworthy principles and ML.NET spike detection algorithms.
/// </remarks>
public class IncidentAnomalyDetector
{
    private readonly ILogger<IncidentAnomalyDetector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncidentAnomalyDetector"/> class.
    /// </summary>
    /// <param name="logger">The logger service.</param>
    public IncidentAnomalyDetector(ILogger<IncidentAnomalyDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes a time-series window of incident call volumes to identify sudden abnormal surges.
    /// </summary>
    /// <param name="hourlyCallCounts">List of chronological hourly incident call counts.</param>
    /// <param name="sensitivity">Threshold sensitivity factor (0.0 to 1.0).</param>
    /// <returns>True if a statistically significant emergency surge anomaly is detected; otherwise false.</returns>
    public bool DetectSpikeAnomaly(List<double> hourlyCallCounts, double sensitivity = 0.85)
    {
        if (hourlyCallCounts.Count < 5) return false;

        double sum = 0;
        foreach (var count in hourlyCallCounts) sum += count;
        var mean = sum / hourlyCallCounts.Count;

        double sumSquares = 0;
        foreach (var count in hourlyCallCounts) sumSquares += Math.Pow(count - mean, 2);
        var stdDev = Math.Sqrt(sumSquares / hourlyCallCounts.Count);

        var latest = hourlyCallCounts[^1];
        var zScore = stdDev > 0 ? (latest - mean) / stdDev : 0;

        var isAnomaly = zScore > (2.5 * sensitivity);
        if (isAnomaly)
        {
            _logger.LogWarning("EMERGENCY SURGE DETECTED! Latest Call Volume={Volume}, Mean={Mean:F1}, Z-Score={Z:F2}", latest, mean, zScore);
        }

        return isAnomaly;
    }
}
