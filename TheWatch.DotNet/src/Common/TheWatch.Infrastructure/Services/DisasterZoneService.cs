using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of disaster zone service with geospatial calculations.
/// Handles GeoJSON validation, geohash extraction, and evacuation route planning.
/// </summary>
public class DisasterZoneService : IDisasterZoneService
{
    private readonly GeohashService _geohashService;

    public DisasterZoneService()
    {
        _geohashService = new GeohashService();
    }

    public bool ValidateGeoJson(string geoJson, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(geoJson))
        {
            errorMessage = "GeoJSON cannot be empty";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                errorMessage = "GeoJSON must have a 'type' property";
                return false;
            }

            string type = typeElement.GetString() ?? "";

            if (type != "Polygon" && type != "MultiPolygon")
            {
                errorMessage = "GeoJSON type must be 'Polygon' or 'MultiPolygon'";
                return false;
            }

            if (!root.TryGetProperty("coordinates", out var coordinates))
            {
                errorMessage = "GeoJSON must have 'coordinates' property";
                return false;
            }

            // Basic validation passed
            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    public IEnumerable<string> ExtractGeohashPrefixes(string geoJson, int precision = 6)
    {
        var prefixes = new HashSet<string>();

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            var coordinates = root.GetProperty("coordinates");

            if (type == "Polygon")
            {
                // Polygon coordinates: [[[lng, lat], [lng, lat], ...]]
                var outerRing = coordinates[0];
                ExtractGeohashesFromRing(outerRing, precision, prefixes);
            }
            else if (type == "MultiPolygon")
            {
                // MultiPolygon coordinates: [[[[lng, lat], [lng, lat], ...]], ...]
                foreach (var polygon in coordinates.EnumerateArray())
                {
                    var outerRing = polygon[0];
                    ExtractGeohashesFromRing(outerRing, precision, prefixes);
                }
            }
        }
        catch
        {
            // Return empty set on error
            return new HashSet<string>();
        }

        return prefixes;
    }

    private void ExtractGeohashesFromRing(JsonElement ring, int precision, HashSet<string> prefixes)
    {
        foreach (var point in ring.EnumerateArray())
        {
            if (point.GetArrayLength() >= 2)
            {
                double lng = point[0].GetDouble();
                double lat = point[1].GetDouble();

                string geohash = _geohashService.Encode(lat, lng, precision);
                prefixes.Add(geohash);

                // Also add neighbors to ensure coverage
                var neighbors = _geohashService.GetNeighbors(geohash);
                foreach (var neighbor in neighbors)
                {
                    prefixes.Add(neighbor);
                }
            }
        }
    }

    public (double latitude, double longitude) CalculateCenterPoint(string geoJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            var coordinates = root.GetProperty("coordinates");

            var allPoints = new List<(double lat, double lng)>();

            if (type == "Polygon")
            {
                var outerRing = coordinates[0];
                CollectPoints(outerRing, allPoints);
            }
            else if (type == "MultiPolygon")
            {
                foreach (var polygon in coordinates.EnumerateArray())
                {
                    var outerRing = polygon[0];
                    CollectPoints(outerRing, allPoints);
                }
            }

            if (allPoints.Count == 0)
            {
                return (0, 0);
            }

            // Calculate centroid
            double sumLat = allPoints.Sum(p => p.lat);
            double sumLng = allPoints.Sum(p => p.lng);

            return (sumLat / allPoints.Count, sumLng / allPoints.Count);
        }
        catch
        {
            return (0, 0);
        }
    }

    private void CollectPoints(JsonElement ring, List<(double lat, double lng)> points)
    {
        foreach (var point in ring.EnumerateArray())
        {
            if (point.GetArrayLength() >= 2)
            {
                double lng = point[0].GetDouble();
                double lat = point[1].GetDouble();
                points.Add((lat, lng));
            }
        }
    }

    public double CalculateRadiusKm(string geoJson, double centerLat, double centerLng)
    {
        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            var coordinates = root.GetProperty("coordinates");

            double maxDistance = 0;

            if (type == "Polygon")
            {
                var outerRing = coordinates[0];
                maxDistance = Math.Max(maxDistance, GetMaxDistanceFromCenter(outerRing, centerLat, centerLng));
            }
            else if (type == "MultiPolygon")
            {
                foreach (var polygon in coordinates.EnumerateArray())
                {
                    var outerRing = polygon[0];
                    maxDistance = Math.Max(maxDistance, GetMaxDistanceFromCenter(outerRing, centerLat, centerLng));
                }
            }

            return maxDistance;
        }
        catch
        {
            return 0;
        }
    }

    private double GetMaxDistanceFromCenter(JsonElement ring, double centerLat, double centerLng)
    {
        double maxDistance = 0;

        foreach (var point in ring.EnumerateArray())
        {
            if (point.GetArrayLength() >= 2)
            {
                double lng = point[0].GetDouble();
                double lat = point[1].GetDouble();

                double distance = CalculateHaversineDistance(centerLat, centerLng, lat, lng);
                maxDistance = Math.Max(maxDistance, distance);
            }
        }

        return maxDistance;
    }

    public bool IsPointInZone(double latitude, double longitude, string geoJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            var coordinates = root.GetProperty("coordinates");

            if (type == "Polygon")
            {
                var outerRing = coordinates[0];
                return IsPointInPolygon(latitude, longitude, outerRing);
            }
            else if (type == "MultiPolygon")
            {
                foreach (var polygon in coordinates.EnumerateArray())
                {
                    var outerRing = polygon[0];
                    if (IsPointInPolygon(latitude, longitude, outerRing))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool IsPointInPolygon(double latitude, double longitude, JsonElement ring)
    {
        // Ray casting algorithm
        var points = new List<(double lat, double lng)>();
        CollectPoints(ring, points);

        if (points.Count < 3)
        {
            return false;
        }

        bool inside = false;

        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            double xi = points[i].lng;
            double yi = points[i].lat;
            double xj = points[j].lng;
            double yj = points[j].lat;

            bool intersect = ((yi > latitude) != (yj > latitude)) &&
                           (longitude < (xj - xi) * (latitude - yi) / (yj - yi) + xi);

            if (intersect)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public int GetSeverityOrder(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "catastrophic" => 5,
            "mandatory_evacuation" => 4,
            "warning" => 3,
            "watch" => 2,
            "advisory" => 1,
            _ => 0
        };
    }

    public async Task<EvacuationRouteResult> CalculateEvacuationRouteAsync(
        double fromLat,
        double fromLng,
        double? toLat,
        double? toLng,
        IEnumerable<string> avoidDisasterTypes,
        bool includeFuelStops,
        bool includeShelters,
        CancellationToken cancellationToken = default)
    {
        // TODO: Integrate with Azure Maps or Google Maps API for route calculation
        // This is a placeholder implementation

        var result = new EvacuationRouteResult
        {
            Summary = "Evacuation route calculation requires external mapping service integration",
            EtaMinutes = 0,
            DistanceKm = 0,
            Warnings = new List<string>
            {
                "Route calculation not yet implemented - requires Azure Maps integration"
            },
            Polyline = "",
            FuelStops = new List<FuelStopInfo>(),
            Shelters = new List<ShelterInfo>()
        };

        // If destination is provided, calculate straight-line distance
        if (toLat.HasValue && toLng.HasValue)
        {
            double distance = CalculateHaversineDistance(fromLat, fromLng, toLat.Value, toLng.Value);
            result.DistanceKm = distance;
            result.EtaMinutes = (int)(distance / 60.0 * 60); // Assume 60 km/h average speed
        }

        return await Task.FromResult(result);
    }

    private double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // Earth's radius in kilometers

        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    private double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
