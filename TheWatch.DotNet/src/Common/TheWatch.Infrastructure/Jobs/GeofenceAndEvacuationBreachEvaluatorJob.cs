using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Jobs;

public sealed record GeofenceAlert(string TargetId, string GeofenceId, double Latitude, double Longitude, bool IsBreach, string Severity);

/// <summary>
/// Background job that evaluates responder/civilian coordinates against active high-risk disaster perimeter geofences.
/// </summary>
public sealed class GeofenceAndEvacuationBreachEvaluatorJob
{
    public Task<List<GeofenceAlert>> EvaluateBreachesAsync(
        IEnumerable<(string TargetId, double Lat, double Lon)> positions,
        double geofenceCenterLat,
        double geofenceCenterLon,
        double radiusMeters,
        string geofenceId,
        CancellationToken cancellationToken = default)
    {
        var alerts = new List<GeofenceAlert>();

        foreach (var (targetId, lat, lon) in positions)
        {
            if (cancellationToken.IsCancellationRequested) break;

            double dist = CalculateDistanceMeters(lat, lon, geofenceCenterLat, geofenceCenterLon);
            bool isInside = dist <= radiusMeters;

            if (isInside)
            {
                alerts.Add(new GeofenceAlert(
                    TargetId: targetId,
                    GeofenceId: geofenceId,
                    Latitude: lat,
                    Longitude: lon,
                    IsBreach: true,
                    Severity: dist < (radiusMeters * 0.5) ? "CRITICAL_PROXIMITY" : "WARNING_BOUNDARY"
                ));
            }
        }

        return Task.FromResult(alerts);
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000; // meters
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }
}
