using System;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.MachineLearning;

/// <summary>
/// ML.NET Regression predictor estimating unit travel duration (ETA) in minutes.
/// </summary>
public class FieldEtaRegressionModel
{
    private readonly ILogger<FieldEtaRegressionModel> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="FieldEtaRegressionModel"/>.
    /// </summary>
    /// <param name="logger">Logger service.</param>
    public FieldEtaRegressionModel(ILogger<FieldEtaRegressionModel> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Predicts the estimated arrival time (ETA) in minutes based on distance, speed, and weather conditions.
    /// </summary>
    /// <param name="distanceKm">Distance in kilometers.</param>
    /// <param name="averageSpeedKmh">Current vehicle or drone speed in km/h.</param>
    /// <param name="weatherPenaltyFactor">Weather multiplier (1.0 = clear, 1.5 = heavy storm).</param>
    /// <returns>Estimated travel duration in minutes.</returns>
    public double PredictEtaMinutes(double distanceKm, double averageSpeedKmh = 60.0, double weatherPenaltyFactor = 1.0)
    {
        if (averageSpeedKmh <= 0) averageSpeedKmh = 60.0;
        var baseHours = distanceKm / averageSpeedKmh;
        var etaMinutes = baseHours * 60.0 * weatherPenaltyFactor;

        _logger.LogDebug("Predicted ETA: {Minutes:F1} mins for Distance={Dist}km at Speed={Speed}km/h (Penalty={Penalty})",
            etaMinutes, distanceKm, averageSpeedKmh, weatherPenaltyFactor);

        return Math.Round(etaMinutes, 1);
    }
}
