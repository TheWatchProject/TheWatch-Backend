using System;

namespace TheWatch.Infrastructure.Telemetry;

public record GeoLocation(double Latitude, double Longitude);

public static class GeospatialCalculator
{
    private const double EarthRadiusKm = 6371.0;

    public static double CalculateDistanceKm(GeoLocation p1, GeoLocation p2)
    {
        var dLat = ToRadians(p2.Latitude - p1.Latitude);
        var dLon = ToRadians(p2.Longitude - p1.Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(p1.Latitude)) * Math.Cos(ToRadians(p2.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    public static bool IsWithinGeofence(GeoLocation point, GeoLocation center, double radiusKm)
    {
        return CalculateDistanceKm(point, center) <= radiusKm;
    }

    private static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);
}