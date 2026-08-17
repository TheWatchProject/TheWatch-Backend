using System.Collections.Concurrent;
using TheWatch.Microservices.Location.LocationService.Models;

namespace TheWatch.Microservices.Location.LocationService.Services;

public interface ILocationEngine
{
    Task<LocationTelemetry> RecordTelemetryAsync(RecordTelemetryRequest request);
    Task<LocationTelemetry?> GetCurrentLocationAsync(string responderId);
    Task<IEnumerable<LocationTelemetry>> GetLocationHistoryAsync(string responderId, int limit = 50);
    Task<IEnumerable<ProximityMatch>> FindNearbyRespondersAsync(NearbyQueryRequest request);
    Task<GeofenceCheckResult> CheckGeofenceAsync(GeofenceCheckRequest request);
    Task<IEnumerable<GeofenceZone>> GetAllGeofencesAsync();
    Task<GeofenceZone> CreateGeofenceAsync(GeofenceZone zone);
}

public class InMemoryLocationEngine : ILocationEngine
{
    private static readonly ConcurrentDictionary<string, LocationTelemetry> LatestPositions = new();
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<LocationTelemetry>> History = new();
    private static readonly ConcurrentDictionary<string, GeofenceZone> Geofences = new();

    static InMemoryLocationEngine()
    {
        // Pre-seed sample active telemetry
        SeedPosition("UNIT-MEDIC-42", "MEDIC-42", 37.7750, -122.4180, 25.0, 45.0, 95);
        SeedPosition("UNIT-FIRE-07", "ENGINE-7", 37.7840, -122.4150, 40.0, 180.0, 90);
        SeedPosition("UNIT-AED-DRONE-1", "AERO-MED-1", 37.7885, -122.4020, 110.0, 270.0, 88);
        SeedPosition("UNIT-HELO-3", "AIR-MEDEVAC-3", 37.7600, -122.3900, 250.0, 315.0, 80);

        // Pre-seed sample geofences
        var gf1 = new GeofenceZone
        {
            Id = "GEO-001",
            Name = "Downtown Chemical Spill Exclusion Perimeter",
            ZoneType = "HotZone",
            CenterLatitude = 37.7749,
            CenterLongitude = -122.4194,
            RadiusMeters = 800.0,
            IsActive = true,
            AssociatedIncidentId = "INC-1001"
        };
        var gf2 = new GeofenceZone
        {
            Id = "GEO-002",
            Name = "Industrial Park Fire Safety Perimeter",
            ZoneType = "WarmZone",
            CenterLatitude = 37.7833,
            CenterLongitude = -122.4167,
            RadiusMeters = 500.0,
            IsActive = true,
            AssociatedIncidentId = "INC-1002"
        };

        Geofences[gf1.Id] = gf1;
        Geofences[gf2.Id] = gf2;
    }

    private static void SeedPosition(string responderId, string callsign, double lat, double lon, double speed, double heading, int battery)
    {
        var tele = new LocationTelemetry
        {
            ResponderId = responderId,
            UnitCallsign = callsign,
            Latitude = lat,
            Longitude = lon,
            AltitudeMeters = 15.0,
            SpeedKmh = speed,
            HeadingDegrees = heading,
            AccuracyMeters = 3.5,
            BatteryPercentage = battery,
            TimestampUtc = DateTime.UtcNow
        };
        LatestPositions[responderId] = tele;
        var q = History.GetOrAdd(responderId, _ => new ConcurrentQueue<LocationTelemetry>());
        q.Enqueue(tele);
    }

    public Task<LocationTelemetry> RecordTelemetryAsync(RecordTelemetryRequest request)
    {
        var telemetry = new LocationTelemetry
        {
            Id = Guid.NewGuid().ToString(),
            ResponderId = request.ResponderId,
            UnitCallsign = request.UnitCallsign,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AltitudeMeters = request.AltitudeMeters,
            SpeedKmh = request.SpeedKmh,
            HeadingDegrees = request.HeadingDegrees,
            AccuracyMeters = request.AccuracyMeters ?? 5.0,
            BatteryPercentage = request.BatteryPercentage,
            TimestampUtc = DateTime.UtcNow
        };

        LatestPositions[request.ResponderId] = telemetry;

        var q = History.GetOrAdd(request.ResponderId, _ => new ConcurrentQueue<LocationTelemetry>());
        q.Enqueue(telemetry);
        while (q.Count > 100)
        {
            q.TryDequeue(out _);
        }

        return Task.FromResult(telemetry);
    }

    public Task<LocationTelemetry?> GetCurrentLocationAsync(string responderId)
    {
        LatestPositions.TryGetValue(responderId, out var telemetry);
        return Task.FromResult(telemetry);
    }

    public Task<IEnumerable<LocationTelemetry>> GetLocationHistoryAsync(string responderId, int limit = 50)
    {
        if (!History.TryGetValue(responderId, out var q))
        {
            return Task.FromResult(Enumerable.Empty<LocationTelemetry>());
        }

        return Task.FromResult(q.Reverse().Take(limit).AsEnumerable());
    }

    public Task<IEnumerable<ProximityMatch>> FindNearbyRespondersAsync(NearbyQueryRequest request)
    {
        var matches = new List<ProximityMatch>();

        foreach (var pos in LatestPositions.Values)
        {
            var distMeters = CalculateDistanceMeters(request.Latitude, request.Longitude, pos.Latitude, pos.Longitude);
            if (distMeters <= request.RadiusMeters)
            {
                var bearing = CalculateBearing(request.Latitude, request.Longitude, pos.Latitude, pos.Longitude);
                matches.Add(new ProximityMatch
                {
                    Telemetry = pos,
                    DistanceMeters = Math.Round(distMeters, 1),
                    BearingDegrees = Math.Round(bearing, 1)
                });
            }
        }

        return Task.FromResult(matches.OrderBy(m => m.DistanceMeters).Take(request.Limit).AsEnumerable());
    }

    public Task<GeofenceCheckResult> CheckGeofenceAsync(GeofenceCheckRequest request)
    {
        var triggered = new List<GeofenceZone>();

        foreach (var zone in Geofences.Values.Where(z => z.IsActive))
        {
            var distance = CalculateDistanceMeters(request.Latitude, request.Longitude, zone.CenterLatitude, zone.CenterLongitude);
            if (distance <= zone.RadiusMeters)
            {
                triggered.Add(zone);
            }
        }

        return Task.FromResult(new GeofenceCheckResult
        {
            IsInsideGeofence = triggered.Count > 0,
            TriggeredZones = triggered
        });
    }

    public Task<IEnumerable<GeofenceZone>> GetAllGeofencesAsync()
    {
        return Task.FromResult(Geofences.Values.AsEnumerable());
    }

    public Task<GeofenceZone> CreateGeofenceAsync(GeofenceZone zone)
    {
        if (string.IsNullOrWhiteSpace(zone.Id)) zone.Id = $"GEO-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        Geofences[zone.Id] = zone;
        return Task.FromResult(zone);
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadius = 6371000.0; // meters
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadius * c;
    }

    private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        var rLat1 = DegreesToRadians(lat1);
        var rLat2 = DegreesToRadians(lat2);
        var dLon = DegreesToRadians(lon2 - lon1);

        var y = Math.Sin(dLon) * Math.Cos(rLat2);
        var x = Math.Cos(rLat1) * Math.Sin(rLat2) - Math.Sin(rLat1) * Math.Cos(rLat2) * Math.Cos(dLon);
        var brng = Math.Atan2(y, x);

        return (RadiansToDegrees(brng) + 360.0) % 360.0;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);
    private static double RadiansToDegrees(double radians) => radians * (180.0 / Math.PI);
}
