namespace TheWatch.Microservices.Location.LocationService.Models;

public class LocationTelemetry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ResponderId { get; set; } = string.Empty;
    public string UnitCallsign { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AltitudeMeters { get; set; }
    public double? SpeedKmh { get; set; }
    public double? HeadingDegrees { get; set; }
    public double? AccuracyMeters { get; set; }
    public int BatteryPercentage { get; set; } = 100;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class RecordTelemetryRequest
{
    public string ResponderId { get; set; } = string.Empty;
    public string UnitCallsign { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AltitudeMeters { get; set; }
    public double? SpeedKmh { get; set; }
    public double? HeadingDegrees { get; set; }
    public double? AccuracyMeters { get; set; }
    public int BatteryPercentage { get; set; } = 100;
}

public class NearbyQueryRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 5000.0;
    public int Limit { get; set; } = 10;
}

public class ProximityMatch
{
    public LocationTelemetry Telemetry { get; set; } = new();
    public double DistanceMeters { get; set; }
    public double BearingDegrees { get; set; }
}

public class GeofenceZone
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string ZoneType { get; set; } = "ExclusionZone"; // ExclusionZone, HotZone, WarmZone, EvacuationPerimeter
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public double RadiusMeters { get; set; }
    public bool IsActive { get; set; } = true;
    public string AssociatedIncidentId { get; set; } = string.Empty;
}

public class GeofenceCheckRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? ResponderId { get; set; }
}

public class GeofenceCheckResult
{
    public bool IsInsideGeofence { get; set; }
    public List<GeofenceZone> TriggeredZones { get; set; } = new();
}
