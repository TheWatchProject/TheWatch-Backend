using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for disaster zone management.
/// Implements disaster zone endpoints from evacuation-api.yaml and hq-admin-api.yaml
/// Manages disaster zone boundaries as GeoJSON with geohash indexing
/// </summary>
public class DisasterZoneFunctions
{
    private readonly ILogger<DisasterZoneFunctions> _logger;
    private readonly IDisasterZoneRepository _disasterZoneRepository;
    private readonly IDisasterZoneService _disasterZoneService;
    private readonly INotificationService _notificationService;
    private readonly GeohashService _geohashService;

    // Valid disaster types
    private static readonly HashSet<string> ValidDisasterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "hurricane", "wildfire", "flood", "earthquake", "chemical_spill",
        "tornado", "tsunami", "volcanic", "other"
    };

    // Valid severity levels (ordered by priority)
    private static readonly Dictionary<string, int> SeverityOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        { "advisory", 1 },
        { "watch", 2 },
        { "warning", 3 },
        { "mandatory_evacuation", 4 },
        { "catastrophic", 5 }
    };

    // Valid evacuation orders
    private static readonly HashSet<string> ValidEvacuationOrders = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "voluntary", "recommended", "mandatory"
    };

    public DisasterZoneFunctions(
        ILogger<DisasterZoneFunctions> logger,
        IDisasterZoneRepository disasterZoneRepository,
        IDisasterZoneService disasterZoneService,
        INotificationService notificationService,
        GeohashService geohashService)
    {
        _logger = logger;
        _disasterZoneRepository = disasterZoneRepository;
        _disasterZoneService = disasterZoneService;
        _notificationService = notificationService;
        _geohashService = geohashService;
    }

    /// <summary>
    /// GET /disaster-zones - List active disaster zones
    /// Public endpoint for users to check if they're in an active disaster zone
    /// </summary>
    [Function("ListDisasterZones")]
    public async Task<HttpResponseData> ListDisasterZones(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "disaster-zones")] HttpRequestData req)
    {
        _logger.LogInformation("Listing disaster zones");

        try
        {
            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var activeOnly = !bool.TryParse(query["active"], out var active) || active; // default true
            var disasterType = query["disaster_type"];
            var severity = query["severity"];

            // Validate disaster_type if specified
            if (!string.IsNullOrEmpty(disasterType) && !ValidDisasterTypes.Contains(disasterType))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = $"Invalid disaster_type. Valid values: {string.Join(", ", ValidDisasterTypes)}" });
                return errorResponse;
            }

            // Validate severity if specified
            if (!string.IsNullOrEmpty(severity) && !SeverityOrder.ContainsKey(severity))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = $"Invalid severity. Valid values: {string.Join(", ", SeverityOrder.Keys)}" });
                return errorResponse;
            }

            // Query zones from repository
            IEnumerable<DisasterZone> zones;
            if (activeOnly)
            {
                zones = await _disasterZoneRepository.GetActiveZonesAsync(disasterType, severity);
            }
            else
            {
                zones = await _disasterZoneRepository.GetActiveZonesAsync();
                if (!string.IsNullOrEmpty(disasterType))
                    zones = zones.Where(z => z.DisasterType.Equals(disasterType, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(severity))
                    zones = zones.Where(z => z.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase));
            }

            // Order by severity DESC (catastrophic > mandatory_evacuation > warning > watch > advisory)
            var orderedZones = zones
                .OrderByDescending(z => SeverityOrder.GetValueOrDefault(z.Severity, 0))
                .ThenBy(z => z.IssuedAt)
                .Select(z => new
                {
                    zoneId = z.ZoneId,
                    name = z.Name,
                    disasterType = z.DisasterType,
                    severity = z.Severity,
                    boundariesGeoJson = JsonSerializer.Deserialize<object>(z.BoundariesGeojson),
                    geohashPrefixes = JsonSerializer.Deserialize<string[]>(z.GeohashPrefixes),
                    centerLat = z.CenterLat,
                    centerLng = z.CenterLng,
                    radiusKm = z.RadiusKm,
                    evacuationOrder = z.EvacuationOrder,
                    issuedBy = z.IssuedBy,
                    issuedAt = z.IssuedAt,
                    expiresAt = z.ExpiresAt,
                    isActive = z.IsActive,
                    affectedPopulationEstimate = z.AffectedPopulationEstimate
                })
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(orderedZones);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing disaster zones");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred while listing disaster zones" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /hq/disaster-zones - Create a new disaster zone (HQ only)
    /// Requires 'hq' or 'admin' role in JWT
    /// </summary>
    [Function("CreateDisasterZone")]
    public async Task<HttpResponseData> CreateDisasterZone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/disaster-zones")] HttpRequestData req)
    {
        _logger.LogInformation("Creating disaster zone");

        try
        {
            // Validate HQ/admin authentication
            var authResult = ValidateHqAdminAuth(req);
            if (!authResult.IsAuthorized)
            {
                var authError = req.CreateResponse(HttpStatusCode.Unauthorized);
                await authError.WriteAsJsonAsync(new { error = authResult.ErrorMessage });
                return authError;
            }

            // Parse request body
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Request body is required" });
                return errorResponse;
            }

            var createRequest = JsonSerializer.Deserialize<DisasterZoneCreateRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (createRequest == null)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
                return errorResponse;
            }

            // Validate name
            if (string.IsNullOrWhiteSpace(createRequest.Name) || createRequest.Name.Length > 200)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Name is required and must be 200 characters or less" });
                return errorResponse;
            }

            // Validate disaster_type
            if (!ValidDisasterTypes.Contains(createRequest.DisasterType))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = $"Invalid disaster_type. Valid values: {string.Join(", ", ValidDisasterTypes)}" });
                return errorResponse;
            }

            // Validate severity
            if (!SeverityOrder.ContainsKey(createRequest.Severity))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = $"Invalid severity. Valid values: {string.Join(", ", SeverityOrder.Keys)}" });
                return errorResponse;
            }

            // Validate evacuation_order
            if (!ValidEvacuationOrders.Contains(createRequest.EvacuationOrder))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = $"Invalid evacuation_order. Valid values: {string.Join(", ", ValidEvacuationOrders)}" });
                return errorResponse;
            }

            // Validate GeoJSON boundaries
            if (string.IsNullOrEmpty(createRequest.BoundariesGeoJson))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "boundariesGeoJson is required" });
                return errorResponse;
            }

            if (!_disasterZoneService.ValidateGeoJson(createRequest.BoundariesGeoJson, out var geoJsonError))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = $"Invalid GeoJSON: {geoJsonError}" });
                return errorResponse;
            }

            // Extract geohash prefixes from GeoJSON boundary (precision 6 for ~1.2km grid)
            var geohashPrefixes = _disasterZoneService.ExtractGeohashPrefixes(createRequest.BoundariesGeoJson, 6).ToList();

            // Calculate center point and radius
            var (centerLat, centerLng) = _disasterZoneService.CalculateCenterPoint(createRequest.BoundariesGeoJson);
            var radiusKm = _disasterZoneService.CalculateRadiusKm(createRequest.BoundariesGeoJson, centerLat, centerLng);
            var centerGeohash = _geohashService.Encode(centerLat, centerLng, 9);

            // Estimate affected population (query users with geohash in zone)
            var affectedUserIds = await _disasterZoneRepository.GetUserIdsInGeohashPrefixesAsync(geohashPrefixes);
            var affectedPopulation = affectedUserIds.Count();

            // Create disaster zone entity
            var zone = new DisasterZone
            {
                ZoneId = Guid.NewGuid(),
                Name = createRequest.Name,
                DisasterType = createRequest.DisasterType.ToLowerInvariant(),
                Severity = createRequest.Severity.ToLowerInvariant(),
                BoundariesGeojson = createRequest.BoundariesGeoJson,
                GeohashPrefixes = JsonSerializer.Serialize(geohashPrefixes),
                CenterLat = centerLat,
                CenterLng = centerLng,
                CenterGeohash = centerGeohash,
                RadiusKm = radiusKm,
                EvacuationOrder = createRequest.EvacuationOrder.ToLowerInvariant(),
                IsActive = true,
                IssuedBy = JwtUtilities.GetUserIdFromClaims(authResult.Principal)?.ToString() ?? string.Empty,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = createRequest.ExpiresAt,
                AffectedPopulationEstimate = affectedPopulation,
                Notes = createRequest.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Save to database
            await _disasterZoneRepository.CreateAsync(zone);

            _logger.LogInformation("Created disaster zone {ZoneId} with {AffectedUsers} affected users", zone.ZoneId, affectedPopulation);

            // Trigger notification to all users in affected geohash prefixes
            if (affectedUserIds.Any())
            {
                var notificationTitle = GetNotificationTitle(zone.Severity, zone.DisasterType);
                var notificationMessage = $"{zone.Name}: {GetEvacuationMessage(zone.EvacuationOrder)}";

                var (sent, failed) = await _notificationService.SendDisasterZoneNotificationAsync(
                    affectedUserIds,
                    zone.ZoneId,
                    notificationTitle,
                    notificationMessage,
                    zone.Severity == "catastrophic" || zone.Severity == "mandatory_evacuation" ? "critical" : "high",
                    new Dictionary<string, string>
                    {
                        { "zoneId", zone.ZoneId.ToString() },
                        { "disasterType", zone.DisasterType },
                        { "severity", zone.Severity },
                        { "evacuationOrder", zone.EvacuationOrder }
                    });

                _logger.LogInformation("Sent {Sent} notifications for zone {ZoneId}, {Failed} failed", sent, zone.ZoneId, failed);
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                zoneId = zone.ZoneId,
                name = zone.Name,
                disasterType = zone.DisasterType,
                severity = zone.Severity,
                boundariesGeoJson = !string.IsNullOrEmpty(zone.BoundariesGeojson) ? JsonSerializer.Deserialize<object>(zone.BoundariesGeojson) : null,
                geohashPrefixes = geohashPrefixes,
                centerLat = zone.CenterLat,
                centerLng = zone.CenterLng,
                radiusKm = zone.RadiusKm,
                evacuationOrder = zone.EvacuationOrder,
                issuedAt = zone.IssuedAt,
                expiresAt = zone.ExpiresAt,
                isActive = zone.IsActive,
                affectedPopulationEstimate = zone.AffectedPopulationEstimate
            });

            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in request body");
            var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await errorResponse.WriteAsJsonAsync(new { error = "Invalid JSON format" });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating disaster zone");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred while creating the disaster zone" });
            return errorResponse;
        }
    }

    /// <summary>
    /// PATCH /hq/disaster-zones/{zoneId} - Update disaster zone (HQ only)
    /// Allows HQ to adjust severity or evacuation order as situation evolves
    /// </summary>
    [Function("UpdateDisasterZone")]
    public async Task<HttpResponseData> UpdateDisasterZone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "hq/disaster-zones/{zoneId}")] HttpRequestData req,
        string zoneId)
    {
        _logger.LogInformation("Updating disaster zone: {ZoneId}", zoneId);

        try
        {
            // Validate HQ/admin authentication
            var authResult = ValidateHqAdminAuth(req);
            if (!authResult.IsAuthorized)
            {
                var authError = req.CreateResponse(HttpStatusCode.Unauthorized);
                await authError.WriteAsJsonAsync(new { error = authResult.ErrorMessage });
                return authError;
            }

            // Parse zoneId as Guid
            if (!Guid.TryParse(zoneId, out var zoneGuid))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Invalid zone ID format" });
                return errorResponse;
            }

            // Parse request body
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Request body is required" });
                return errorResponse;
            }

            var updateRequest = JsonSerializer.Deserialize<DisasterZoneUpdateRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateRequest == null)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
                return errorResponse;
            }

            // Retrieve disaster zone record
            var zone = await _disasterZoneRepository.GetByIdAsync(zoneGuid);
            if (zone == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Disaster zone not found" });
                return notFoundResponse;
            }

            // Validate zone is still active
            if (!zone.IsActive)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await errorResponse.WriteAsJsonAsync(new { error = "Cannot update an inactive disaster zone" });
                return errorResponse;
            }

            // Track if severity or evacuation order increased for notification
            var previousSeverityOrder = SeverityOrder.GetValueOrDefault(zone.Severity, 0);
            var previousEvacuationOrder = zone.EvacuationOrder;
            var shouldNotify = false;

            // Update severity if provided
            if (!string.IsNullOrEmpty(updateRequest.Severity))
            {
                if (!SeverityOrder.ContainsKey(updateRequest.Severity))
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await errorResponse.WriteAsJsonAsync(new { error = $"Invalid severity. Valid values: {string.Join(", ", SeverityOrder.Keys)}" });
                    return errorResponse;
                }

                var newSeverityOrder = SeverityOrder[updateRequest.Severity];
                if (newSeverityOrder > previousSeverityOrder)
                {
                    shouldNotify = true;
                }
                zone.Severity = updateRequest.Severity.ToLowerInvariant();
            }

            // Update evacuation order if provided
            if (!string.IsNullOrEmpty(updateRequest.EvacuationOrder))
            {
                if (!ValidEvacuationOrders.Contains(updateRequest.EvacuationOrder))
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await errorResponse.WriteAsJsonAsync(new { error = $"Invalid evacuation_order. Valid values: {string.Join(", ", ValidEvacuationOrders)}" });
                    return errorResponse;
                }

                // Check if evacuation order is increasing in urgency
                var evacuationOrderPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "none", 0 },
                    { "voluntary", 1 },
                    { "recommended", 2 },
                    { "mandatory", 3 }
                };

                if (evacuationOrderPriority.GetValueOrDefault(updateRequest.EvacuationOrder, 0) >
                    evacuationOrderPriority.GetValueOrDefault(previousEvacuationOrder, 0))
                {
                    shouldNotify = true;
                }
                zone.EvacuationOrder = updateRequest.EvacuationOrder.ToLowerInvariant();
            }

            // Update expires_at if provided
            if (updateRequest.ExpiresAt.HasValue)
            {
                zone.ExpiresAt = updateRequest.ExpiresAt.Value;
            }

            // Update notes if provided
            if (updateRequest.Notes != null)
            {
                zone.Notes = updateRequest.Notes;
            }

            zone.UpdatedAt = DateTime.UtcNow;

            // Save to database
            await _disasterZoneRepository.UpdateAsync(zone);

            _logger.LogInformation("Updated disaster zone {ZoneId}", zone.ZoneId);

            // If severity or evacuation order increased, send critical notification
            if (shouldNotify)
            {
                var geohashPrefixes = JsonSerializer.Deserialize<string[]>(zone.GeohashPrefixes) ?? Array.Empty<string>();
                var affectedUserIds = await _disasterZoneRepository.GetUserIdsInGeohashPrefixesAsync(geohashPrefixes);

                if (affectedUserIds.Any())
                {
                    var notificationTitle = $"⚠️ ALERT UPGRADED: {zone.DisasterType.ToUpperInvariant()}";
                    var notificationMessage = $"{zone.Name}: Severity now {zone.Severity.ToUpperInvariant()}. {GetEvacuationMessage(zone.EvacuationOrder)}";

                    await _notificationService.SendDisasterZoneNotificationAsync(
                        affectedUserIds,
                        zone.ZoneId,
                        notificationTitle,
                        notificationMessage,
                        "critical",
                        new Dictionary<string, string>
                        {
                            { "zoneId", zone.ZoneId.ToString() },
                            { "disasterType", zone.DisasterType },
                            { "severity", zone.Severity },
                            { "evacuationOrder", zone.EvacuationOrder },
                            { "upgraded", "true" }
                        });

                    _logger.LogInformation("Sent upgrade notification for zone {ZoneId} to {UserCount} users", zone.ZoneId, affectedUserIds.Count());
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                zoneId = zone.ZoneId,
                name = zone.Name,
                disasterType = zone.DisasterType,
                severity = zone.Severity,
                evacuationOrder = zone.EvacuationOrder,
                issuedAt = zone.IssuedAt,
                expiresAt = zone.ExpiresAt,
                isActive = zone.IsActive,
                updatedAt = zone.UpdatedAt
            });

            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in request body");
            var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await errorResponse.WriteAsJsonAsync(new { error = "Invalid JSON format" });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating disaster zone {ZoneId}", zoneId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred while updating the disaster zone" });
            return errorResponse;
        }
    }

    /// <summary>
    /// DELETE /hq/disaster-zones/{zoneId} - Deactivate disaster zone (HQ only)
    /// Marks zone as inactive when disaster has passed (all-clear notification)
    /// </summary>
    [Function("DeactivateDisasterZone")]
    public async Task<HttpResponseData> DeactivateDisasterZone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "hq/disaster-zones/{zoneId}")] HttpRequestData req,
        string zoneId)
    {
        _logger.LogInformation("Deactivating disaster zone: {ZoneId}", zoneId);

        try
        {
            // Validate HQ/admin authentication
            var authResult = ValidateHqAdminAuth(req);
            if (!authResult.IsAuthorized)
            {
                var authError = req.CreateResponse(HttpStatusCode.Unauthorized);
                await authError.WriteAsJsonAsync(new { error = authResult.ErrorMessage });
                return authError;
            }

            // Parse zoneId as Guid
            if (!Guid.TryParse(zoneId, out var zoneGuid))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Invalid zone ID format" });
                return errorResponse;
            }

            // Retrieve disaster zone record
            var zone = await _disasterZoneRepository.GetByIdAsync(zoneGuid);
            if (zone == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Disaster zone not found" });
                return notFoundResponse;
            }

            // Check if already inactive
            if (!zone.IsActive)
            {
                _logger.LogInformation("Disaster zone {ZoneId} is already inactive", zoneId);
                return req.CreateResponse(HttpStatusCode.NoContent);
            }

            // Get affected users before deactivation
            var geohashPrefixes = JsonSerializer.Deserialize<string[]>(zone.GeohashPrefixes) ?? Array.Empty<string>();
            var affectedUserIds = await _disasterZoneRepository.GetUserIdsInGeohashPrefixesAsync(geohashPrefixes);

            // Deactivate zone
            zone.IsActive = false;
            zone.ExpiresAt ??= DateTime.UtcNow;
            zone.UpdatedAt = DateTime.UtcNow;

            await _disasterZoneRepository.DeactivateAsync(zoneGuid);

            _logger.LogInformation("Deactivated disaster zone {ZoneId}", zone.ZoneId);

            // Send "all clear" notification to users in affected area
            if (affectedUserIds.Any())
            {
                var (sent, failed) = await _notificationService.SendDisasterZoneAllClearAsync(
                    affectedUserIds,
                    zone.ZoneId,
                    zone.Name);

                _logger.LogInformation("Sent all-clear notification for zone {ZoneId} to {Sent} users, {Failed} failed",
                    zone.ZoneId, sent, failed);
            }

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating disaster zone {ZoneId}", zoneId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred while deactivating the disaster zone" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /hq/disaster-zones/{zoneId}/notify - Send notification to users in disaster zone
    /// Allows HQ to broadcast emergency messages to affected area
    /// </summary>
    [Function("NotifyDisasterZone")]
    public async Task<HttpResponseData> NotifyDisasterZone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/disaster-zones/{zoneId}/notify")] HttpRequestData req,
        string zoneId)
    {
        _logger.LogInformation("Sending notification to disaster zone: {ZoneId}", zoneId);

        try
        {
            // Validate HQ/admin authentication
            var authResult = ValidateHqAdminAuth(req);
            if (!authResult.IsAuthorized)
            {
                var authError = req.CreateResponse(HttpStatusCode.Unauthorized);
                await authError.WriteAsJsonAsync(new { error = authResult.ErrorMessage });
                return authError;
            }

            // Parse zoneId as Guid
            if (!Guid.TryParse(zoneId, out var zoneGuid))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Invalid zone ID format" });
                return errorResponse;
            }

            // Parse notification payload from request body
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Request body is required" });
                return errorResponse;
            }

            var notifyRequest = JsonSerializer.Deserialize<DisasterZoneNotifyRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (notifyRequest == null || string.IsNullOrWhiteSpace(notifyRequest.Message))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Message is required" });
                return errorResponse;
            }

            // Validate priority
            var validPriorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "normal", "high", "critical" };
            var priority = string.IsNullOrEmpty(notifyRequest.Priority) ? "high" : notifyRequest.Priority;
            if (!validPriorities.Contains(priority))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = $"Invalid priority. Valid values: {string.Join(", ", validPriorities)}" });
                return errorResponse;
            }

            // Retrieve disaster zone record
            var zone = await _disasterZoneRepository.GetByIdAsync(zoneGuid);
            if (zone == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Disaster zone not found" });
                return notFoundResponse;
            }

            // Validate zone is still active
            if (!zone.IsActive)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await errorResponse.WriteAsJsonAsync(new { error = "Cannot send notifications to an inactive disaster zone" });
                return errorResponse;
            }

            // Query users with location geohash matching zone's geohash prefixes
            var geohashPrefixes = JsonSerializer.Deserialize<string[]>(zone.GeohashPrefixes) ?? Array.Empty<string>();
            var affectedUserIds = await _disasterZoneRepository.GetUserIdsInGeohashPrefixesAsync(geohashPrefixes);
            var userCount = affectedUserIds.Count();

            if (!affectedUserIds.Any())
            {
                _logger.LogWarning("No users found in disaster zone {ZoneId}", zoneId);
                var emptyResponse = req.CreateResponse(HttpStatusCode.OK);
                await emptyResponse.WriteAsJsonAsync(new
                {
                    zoneId = zone.ZoneId,
                    message = notifyRequest.Message,
                    priority,
                    usersNotified = 0,
                    notificationsSent = 0,
                    deliveryFailures = 0,
                    sentAt = DateTime.UtcNow
                });
                return emptyResponse;
            }

            // Build notification title
            var notificationTitle = !string.IsNullOrEmpty(notifyRequest.Title)
                ? notifyRequest.Title
                : $"🚨 {zone.DisasterType.ToUpperInvariant()} ALERT";

            // Send push notifications via notification queue with specified priority
            var (sent, failed) = await _notificationService.SendDisasterZoneNotificationAsync(
                affectedUserIds,
                zone.ZoneId,
                notificationTitle,
                notifyRequest.Message,
                priority,
                new Dictionary<string, string>
                {
                    { "zoneId", zone.ZoneId.ToString() },
                    { "disasterType", zone.DisasterType },
                    { "severity", zone.Severity },
                    { "evacuationOrder", zone.EvacuationOrder },
                    { "action", notifyRequest.Action ?? "view_zone" }
                });

            _logger.LogInformation("Sent {Sent} notifications for zone {ZoneId}, {Failed} failed", sent, zone.ZoneId, failed);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                zoneId = zone.ZoneId,
                message = notifyRequest.Message,
                priority,
                usersNotified = userCount,
                notificationsSent = sent,
                deliveryFailures = failed,
                sentAt = DateTime.UtcNow
            });

            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in request body");
            var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await errorResponse.WriteAsJsonAsync(new { error = "Invalid JSON format" });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to disaster zone {ZoneId}", zoneId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred while sending notifications" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /routes/evacuation-route - Calculate safe evacuation route
    /// Returns route guidance away from disaster boundaries
    /// Integrates with Azure Maps or Google Maps for route calculation
    /// </summary>
    [Function("CalculateEvacuationRoute")]
    public async Task<HttpResponseData> CalculateEvacuationRoute(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "routes/evacuation-route")] HttpRequestData req)
    {
        _logger.LogInformation("Calculating evacuation route");

        try
        {
            // Validate authentication
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);
            if (claims == null)
            {
                var authError = req.CreateResponse(HttpStatusCode.Unauthorized);
                await authError.WriteAsJsonAsync(new { error = "Authentication required" });
                return authError;
            }

            // Check Idempotency-Key header
            var idempotencyKey = req.Headers.TryGetValues("Idempotency-Key", out var keys)
                ? keys.FirstOrDefault()
                : null;

            if (string.IsNullOrEmpty(idempotencyKey))
            {
                _logger.LogWarning("Evacuation route request without Idempotency-Key");
                // Continue but log warning - idempotency is recommended but not required
            }

            // Parse request body
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Request body is required" });
                return errorResponse;
            }

            var routeRequest = JsonSerializer.Deserialize<EvacuationRouteRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (routeRequest == null)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
                return errorResponse;
            }

            // Validate origin coordinates
            if (routeRequest.FromLat < -90 || routeRequest.FromLat > 90 ||
                routeRequest.FromLng < -180 || routeRequest.FromLng > 180)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Invalid origin coordinates" });
                return errorResponse;
            }

            // Validate destination coordinates if provided
            if (routeRequest.ToLat.HasValue && routeRequest.ToLng.HasValue)
            {
                if (routeRequest.ToLat < -90 || routeRequest.ToLat > 90 ||
                    routeRequest.ToLng < -180 || routeRequest.ToLng > 180)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await errorResponse.WriteAsJsonAsync(new { error = "Invalid destination coordinates" });
                    return errorResponse;
                }
            }

            // Validate avoid disaster types if provided
            var avoidDisasterTypes = routeRequest.AvoidDisasterTypes ?? new List<string>();
            foreach (var disasterType in avoidDisasterTypes)
            {
                if (!ValidDisasterTypes.Contains(disasterType))
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await errorResponse.WriteAsJsonAsync(new { error = $"Invalid disaster type '{disasterType}'. Valid values: {string.Join(", ", ValidDisasterTypes)}" });
                    return errorResponse;
                }
            }

            // If no specific disaster types, avoid all active disasters
            if (!avoidDisasterTypes.Any())
            {
                avoidDisasterTypes = ValidDisasterTypes.ToList();
            }

            // Calculate route using disaster zone service
            var routeResult = await _disasterZoneService.CalculateEvacuationRouteAsync(
                routeRequest.FromLat,
                routeRequest.FromLng,
                routeRequest.ToLat,
                routeRequest.ToLng,
                avoidDisasterTypes,
                routeRequest.IncludeFuelStops,
                routeRequest.IncludeShelters);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                summary = routeResult.Summary,
                etaMinutes = routeResult.EtaMinutes,
                distanceKm = routeResult.DistanceKm,
                warnings = routeResult.Warnings,
                polyline = routeResult.Polyline,
                fuelStops = routeResult.FuelStops.Select(f => new
                {
                    latitude = f.Latitude,
                    longitude = f.Longitude,
                    address = f.Address,
                    distanceFromStartKm = f.DistanceFromStartKm,
                    hasFuel = f.HasFuel,
                    waitTimeMinutes = f.WaitTimeMinutes
                }),
                shelters = routeResult.Shelters.Select(s => new
                {
                    shelterId = s.ShelterId,
                    name = s.Name,
                    location = new
                    {
                        latitude = s.Latitude,
                        longitude = s.Longitude,
                        address = s.Address
                    },
                    capacity = new
                    {
                        total = s.TotalCapacity,
                        available = s.AvailableCapacity
                    },
                    distanceFromStartKm = s.DistanceFromStartKm,
                    acceptsPets = s.AcceptsPets
                })
            });

            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in request body");
            var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await errorResponse.WriteAsJsonAsync(new { error = "Invalid JSON format" });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating evacuation route");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An error occurred while calculating the evacuation route" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Timer trigger to check for expired disaster zones
    /// Runs every hour to expire zones and send all-clear notifications
    /// Corresponds to "Disaster Zone Expiration Checker" background job in todo.md
    /// </summary>
    [Function("DisasterZoneExpirationChecker")]
    public async Task CheckDisasterZoneExpiration(
        [TimerTrigger("0 0 * * * *")] TimerInfo timer,
        FunctionContext context)
    {
        _logger.LogInformation("Checking for expired disaster zones");

        // TODO: Implement disaster zone expiration logic
        // 1. Query disaster_zones where:
        //    - is_active = true
        //    - expires_at < NOW()
        // 2. For each expired zone:
        //    a. Update is_active = false
        //    b. Query users in zone (by geohash_prefixes)
        //    c. Send "all clear" notification
        //    d. Archive zone data for historical records
        // 3. Log expiration metrics

        _logger.LogInformation("Disaster zone expiration check complete");
    }

    #region Helper Methods

    /// <summary>
    /// Validates that the request is from an authenticated HQ or admin user.
    /// </summary>
    private static (bool IsAuthorized, string? ErrorMessage, ClaimsPrincipal? Principal) ValidateHqAdminAuth(HttpRequestData req)
    {
        // TODO: Implement actual JWT validation
        // For now, return authorized for development
        // In production, validate JWT from Authorization header and check for 'hq' or 'admin' role
        var authHeader = req.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Missing or invalid Authorization header", null);
        }

        // TODO: Validate JWT token and extract claims
        // For development, assume authorized
        return (true, null, null);
    }

    /// <summary>
    /// Gets the notification title based on severity and disaster type.
    /// </summary>
    private static string GetNotificationTitle(string severity, string disasterType) =>
        $"🚨 {disasterType.ToUpperInvariant()} ALERT - {severity.ToUpperInvariant()}";

    /// <summary>
    /// Gets the notification title based on severity.
    /// </summary>
    private static string GetNotificationTitle(string severity) => severity.ToLowerInvariant() switch
    {
        "catastrophic" => "🚨 CATASTROPHIC EMERGENCY - EVACUATE NOW",
        "mandatory_evacuation" => "⚠️ MANDATORY EVACUATION ORDER",
        "warning" => "⚠️ Disaster Warning",
        "watch" => "ℹ️ Disaster Watch",
        "advisory" => "ℹ️ Disaster Advisory",
        _ => "⚠️ Disaster Zone Alert"
    };

    /// <summary>
    /// Gets the evacuation message based on disaster type and severity.
    /// </summary>
    private static string GetEvacuationMessage(string disasterType, string severity, string zoneName) => severity.ToLowerInvariant() switch
    {
        "catastrophic" or "mandatory_evacuation" => 
            $"Immediate evacuation required from {zoneName}. {GetDisasterTypeMessage(disasterType)} Leave the area immediately and proceed to the nearest shelter.",
        "warning" => 
            $"A {disasterType.Replace("_", " ")} warning has been issued for {zoneName}. Prepare to evacuate if conditions worsen.",
        "watch" => 
            $"A {disasterType.Replace("_", " ")} watch is in effect for {zoneName}. Monitor local news and be prepared to evacuate.",
        _ => 
            $"An advisory has been issued for {zoneName} regarding {disasterType.Replace("_", " ")} conditions."
    };

    private static string GetEvacuationMessage(string? evacuationOrder) => evacuationOrder?.ToLowerInvariant() switch
    {
        "mandatory" => "Mandatory evacuation ordered. Leave the area immediately.",
        "voluntary" => "Voluntary evacuation recommended. Prepare to leave if conditions worsen.",
        "shelter_in_place" => "Shelter in place immediately.",
        _ => "Stay alert and monitor emergency broadcasts."
    };

    private static string GetDisasterTypeMessage(string disasterType) => disasterType.ToLowerInvariant() switch
    {
        "hurricane" => "Hurricane conditions are imminent.",
        "wildfire" => "Wildfire is approaching your area.",
        "flood" => "Flooding is expected in your area.",
        "earthquake" => "Significant seismic activity detected.",
        "chemical_spill" => "Hazardous materials have been released.",
        "tornado" => "Tornado conditions are possible.",
        "tsunami" => "Tsunami warning in effect.",
        "volcanic" => "Volcanic activity detected.",
        _ => "Emergency conditions exist."
    };

    #endregion
}

#region Request/Response DTOs

/// <summary>
/// Request DTO for creating a disaster zone.
/// </summary>
public class DisasterZoneCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisasterType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? BoundariesGeoJson { get; set; }
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public double? RadiusKm { get; set; }
    public string EvacuationOrder { get; set; } = "none";
    public DateTime? ExpiresAt { get; set; }
    public int? AffectedPopulationEstimate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Request DTO for updating a disaster zone.
/// </summary>
public class DisasterZoneUpdateRequest
{
    public string? Name { get; set; }
    public string? Severity { get; set; }
    public string? BoundariesGeoJson { get; set; }
    public string? EvacuationOrder { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? AffectedPopulationEstimate { get; set; }
    public string? Notes { get; set; }
    public bool? IsActive { get; set; }
}

public class DisasterZoneNotifyRequest
{
    public string Message { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Priority { get; set; }
    public string? Action { get; set; }
}

public class EvacuationRouteRequest
{
    public double FromLat { get; set; }
    public double FromLng { get; set; }
    public double? ToLat { get; set; }
    public double? ToLng { get; set; }
    public List<string>? AvoidDisasterTypes { get; set; }
    public bool IncludeFuelStops { get; set; }
    public bool IncludeShelters { get; set; }
    public string? Mode { get; set; }
}

#endregion
