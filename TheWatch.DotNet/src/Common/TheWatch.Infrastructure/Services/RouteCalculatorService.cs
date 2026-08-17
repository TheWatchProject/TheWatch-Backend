using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service for calculating safe evacuation routes avoiding disaster zones.
/// </summary>
public class RouteCalculatorService : IRouteCalculatorService
{
    private readonly IDisasterZoneRepository _disasterZoneRepository;
    private readonly IEvacuationRepository _evacuationRepository;
    private readonly GeohashService _geohashService;
    private readonly ILogger<RouteCalculatorService> _logger;

    public RouteCalculatorService(
        IDisasterZoneRepository disasterZoneRepository,
        IEvacuationRepository evacuationRepository,
        ILogger<RouteCalculatorService> logger)
    {
        _disasterZoneRepository = disasterZoneRepository;
        _evacuationRepository = evacuationRepository;
        _geohashService = new GeohashService();
        _logger = logger;
    }

    public async Task<EvacuationRouteResponse> CalculateRouteAsync(
        double fromLat,
        double fromLng,
        double? toLat,
        double? toLng,
        IEnumerable<string> avoidDisasterTypes,
        bool includeFuelStops = true,
        bool includeShelters = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Calculating evacuation route from ({FromLat}, {FromLng}) to ({ToLat}, {ToLng})",
            fromLat, fromLng, toLat, toLng);

        // Get active disaster zones for the specified types
        var disasterZones = await _disasterZoneRepository.GetActiveZonesByTypesAsync(
            avoidDisasterTypes,
            cancellationToken);

        var warnings = new List<string>();

        // Check if starting location is in a disaster zone
        var startZones = await GetDisasterZonesAtLocationAsync(fromLat, fromLng, avoidDisasterTypes, cancellationToken);
        if (startZones.Any())
        {
            warnings.Add($"Starting location is in active {string.Join(", ", startZones.Select(z => z.DisasterType))} zone(s)");
        }

        // If no destination, find nearest safe location
        if (!toLat.HasValue || !toLng.HasValue)
        {
            (toLat, toLng) = await GetNearestSafeLocationAsync(fromLat, fromLng, avoidDisasterTypes, cancellationToken);
            warnings.Add("No destination specified, routing to nearest safe area");
        }

        // Check if destination is in a disaster zone
        var destZones = await GetDisasterZonesAtLocationAsync(toLat.Value, toLng.Value, avoidDisasterTypes, cancellationToken);
        if (destZones.Any())
        {
            warnings.Add($"Destination is in active {string.Join(", ", destZones.Select(z => z.DisasterType))} zone(s)");
        }

        // Calculate distance and ETA
        var distance = _geohashService.CalculateDistance(fromLat, fromLng, toLat.Value, toLng.Value);
        var distanceKm = distance / 1000;
        var etaMinutes = (int)(distanceKm / 60 * 60); // Assuming avg 60 km/h

        // Generate basic polyline (in production, use Google Maps / Azure Maps API)
        var polyline = GenerateBasicPolyline(fromLat, fromLng, toLat.Value, toLng.Value);

        var response = new EvacuationRouteResponse
        {
            Summary = $"Evacuation route from current location to safe area ({distanceKm:F1} km)",
            EtaMinutes = etaMinutes,
            DistanceKm = distanceKm,
            Warnings = warnings,
            Polyline = polyline,
            FuelStops = new List<RouteLocation>(),
            Shelters = new List<ShelterInfo>()
        };

        // Add shelters along route if requested
        if (includeShelters)
        {
            var sheltersNearRoute = await FindSheltersAlongRouteAsync(fromLat, fromLng, toLat.Value, toLng.Value, cancellationToken);
            response.Shelters = sheltersNearRoute.ToList();
        }

        // Add warnings for disaster zones along route
        foreach (var zone in disasterZones)
        {
            var zoneGeohash = zone.CenterGeohash;
            var (zoneLat, zoneLng) = _geohashService.GetCenter(zoneGeohash);

            // Simple check: if zone center is near route, add warning
            var distanceToZone = _geohashService.CalculateDistance(fromLat, fromLng, zoneLat, zoneLng);
            if (distanceToZone < zone.RadiusKm.GetValueOrDefault(10) * 1000 * 1.5)
            {
                warnings.Add($"{zone.DisasterType.ToUpper()} zone detected near route: {zone.Name} (Severity: {zone.Severity})");
            }
        }

        return response;
    }

    public async Task<IEnumerable<DisasterZone>> GetDisasterZonesAtLocationAsync(
        double latitude,
        double longitude,
        IEnumerable<string>? disasterTypes = null,
        CancellationToken cancellationToken = default)
    {
        var geohash = _geohashService.Encode(latitude, longitude, 6);

        var zones = await _disasterZoneRepository.GetZonesContainingGeohashAsync(geohash, cancellationToken);

        if (disasterTypes != null && disasterTypes.Any())
        {
            zones = zones.Where(z => disasterTypes.Contains(z.DisasterType));
        }

        return zones;
    }

    public async Task<(double latitude, double longitude)> GetNearestSafeLocationAsync(
        double fromLat,
        double fromLng,
        IEnumerable<string> avoidDisasterTypes,
        CancellationToken cancellationToken = default)
    {
        // Get all active disaster zones
        var disasterZones = await _disasterZoneRepository.GetActiveZonesByTypesAsync(
            avoidDisasterTypes,
            cancellationToken);

        // Simple heuristic: move 50km in direction away from nearest zone
        if (!disasterZones.Any())
        {
            // No active zones, current location is safe
            return (fromLat, fromLng);
        }

        var nearestZone = disasterZones
            .OrderBy(z => _geohashService.CalculateDistance(fromLat, fromLng, z.CenterLat, z.CenterLng))
            .First();

        // Calculate direction away from zone center
        var bearing = CalculateBearing(nearestZone.CenterLat, nearestZone.CenterLng, fromLat, fromLng);

        // Move 50km in that direction
        var safeLocation = CalculateDestination(fromLat, fromLng, bearing, 50000); // 50km

        _logger.LogInformation(
            "Calculated safe location at ({SafeLat}, {SafeLng}) away from {ZoneType} zone",
            safeLocation.latitude, safeLocation.longitude, nearestZone.DisasterType);

        return safeLocation;
    }

    // Private helper methods

    private string GenerateBasicPolyline(double lat1, double lng1, double lat2, double lng2)
    {
        // In production, use Google Encoded Polyline or Azure Maps polyline
        // For now, return a simple encoded string
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $"{lat1},{lng1}|{lat2},{lng2}"));
    }

    private async Task<IEnumerable<ShelterInfo>> FindSheltersAlongRouteAsync(
        double fromLat,
        double fromLng,
        double toLat,
        double toLng,
        CancellationToken cancellationToken)
    {
        // Find shelters near midpoint of route
        var midLat = (fromLat + toLat) / 2;
        var midLng = (fromLng + toLng) / 2;
        var midGeohash = _geohashService.Encode(midLat, midLng, 5);

        var shelters = await _evacuationRepository.GetAvailableSheltersInAreaAsync(midGeohash, cancellationToken);

        return shelters.Take(3).Select(s => new ShelterInfo
        {
            ShelterId = s.ShelterId,
            Name = s.Name ?? "Emergency Shelter",
            Latitude = s.LocationLat,
            Longitude = s.LocationLng,
            Address = string.Empty, // Would be populated from geocoding service
            TotalCapacity = s.Capacity,
            AvailableCapacity = s.Capacity - s.CurrentOccupancy,
            AcceptsPets = s.AcceptsPets,
            DistanceFromStartKm = 0
        });
    }

    private double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        var dLon = ToRadians(lon2 - lon1);
        var y = Math.Sin(dLon) * Math.Cos(ToRadians(lat2));
        var x = Math.Cos(ToRadians(lat1)) * Math.Sin(ToRadians(lat2)) -
                Math.Sin(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Cos(dLon);
        var bearing = Math.Atan2(y, x);
        return (ToDegrees(bearing) + 360) % 360;
    }

    private (double latitude, double longitude) CalculateDestination(
        double lat,
        double lon,
        double bearing,
        double distanceMeters)
    {
        const double earthRadiusMeters = 6371000;
        var angularDistance = distanceMeters / earthRadiusMeters;
        var bearingRad = ToRadians(bearing);
        var latRad = ToRadians(lat);
        var lonRad = ToRadians(lon);

        var newLatRad = Math.Asin(
            Math.Sin(latRad) * Math.Cos(angularDistance) +
            Math.Cos(latRad) * Math.Sin(angularDistance) * Math.Cos(bearingRad));

        var newLonRad = lonRad + Math.Atan2(
            Math.Sin(bearingRad) * Math.Sin(angularDistance) * Math.Cos(latRad),
            Math.Cos(angularDistance) - Math.Sin(latRad) * Math.Sin(newLatRad));

        return (ToDegrees(newLatRad), ToDegrees(newLonRad));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}
