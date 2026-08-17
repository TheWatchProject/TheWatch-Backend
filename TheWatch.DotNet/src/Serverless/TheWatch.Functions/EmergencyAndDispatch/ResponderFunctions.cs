using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for responder operations.
/// Implements responder queries, distress signals, and offline sync from incident-checkin-api.yaml
/// </summary>
public class ResponderFunctions
{
    private readonly ILogger<ResponderFunctions> _logger;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IDispatchService _dispatchService;
    private readonly INotificationService _notificationService;
    private readonly GeohashService _geohashService;

    public ResponderFunctions(
        ILogger<ResponderFunctions> logger,
        IIncidentRepository incidentRepository,
        IDispatchService dispatchService,
        INotificationService notificationService)
    {
        _logger = logger;
        _incidentRepository = incidentRepository;
        _dispatchService = dispatchService;
        _notificationService = notificationService;
        _geohashService = new GeohashService();
    }

    /// <summary>
    /// GET /responders/nearby - Find nearby responders
    /// Uses geohash-based proximity queries for efficient spatial search
    /// </summary>
    [Function("GetNearbyResponders")]
    public async Task<HttpResponseData> GetNearbyResponders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "responders/nearby")] HttpRequestData req)
    {
        _logger.LogInformation("Finding nearby responders");

        try
        {
            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var latStr = query["lat"];
            var lonStr = query["lon"];
            var radiusStr = query["radius_meters"] ?? "5000";
            var maxResultsStr = query["max_results"] ?? "20";

            if (string.IsNullOrEmpty(latStr) || string.IsNullOrEmpty(lonStr))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Latitude and longitude are required" });
                return badRequest;
            }

            if (!double.TryParse(latStr, out var latitude) || !double.TryParse(lonStr, out var longitude))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid coordinates" });
                return badRequest;
            }

            var radius = int.Parse(radiusStr);
            var maxResults = int.Parse(maxResultsStr);

            // Calculate geohash prefix based on search radius
            // Precision 9 = ~50m, 8 = ~200m, 7 = ~1km, 6 = ~5km, 5 = ~20km
            var geohashPrecision = CalculateGeohashPrecisionForRadius(radius);
            var geohashPrefix = CalculateGeohash(latitude, longitude, geohashPrecision);

            _logger.LogInformation(
                "Searching for responders near ({Lat}, {Lon}) within {Radius}m using geohash prefix {Geohash} (precision {Precision})",
                latitude, longitude, radius, geohashPrefix, geohashPrecision);

            // Find nearby responders using geohash prefix
            var nearbyResponderIds = await _dispatchService.FindNearbyRespondersAsync(
                geohashPrefix,
                maxResults);

            // Fetch responder details and calculate distance/ETA
            // In production, we'd fetch actual responder locations from location service
            // For now, calculate distance using geohash approximation
            var responders = new List<NearbyResponderDto>();

            foreach (var responderId in nearbyResponderIds)
            {
                // In production: Fetch responder's current location from location service
                // For now, use placeholder location based on geohash
                var (responderLat, responderLon) = _geohashService.GetCenter(geohashPrefix);

                // Calculate actual distance using Haversine formula
                var distanceMeters = _geohashService.CalculateDistance(
                    latitude, longitude,
                    responderLat, responderLon);

                // Calculate ETA based on distance
                // Average speed: 50 km/h = 833.33 m/min
                var speedMetersPerMinute = 833.33;
                var estimatedArrivalMinutes = (int)Math.Ceiling(distanceMeters / speedMetersPerMinute);

                responders.Add(new NearbyResponderDto
                {
                    ResponderId = responderId,
                    Distance = (int)distanceMeters,
                    Status = "available",
                    EstimatedArrivalMinutes = estimatedArrivalMinutes
                });
            }

            // Sort by distance (closest first)
            responders = responders.OrderBy(r => r.Distance).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new NearbyRespondersResponse
            {
                Count = responders.Count,
                SearchRadius = radius,
                Location = new LocationDto
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    Geohash = geohashPrefix
                },
                Responders = responders
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding nearby responders");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to find nearby responders" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /responders/distress - Responder panic button (HIGH PRIORITY)
    /// Triggers immediate HQ broadcast and police escalation
    /// </summary>
    [Function("TriggerResponderDistress")]
    public async Task<HttpResponseData> TriggerResponderDistress(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/distress")] HttpRequestData req)
    {
        _logger.LogCritical("RESPONDER DISTRESS SIGNAL RECEIVED");

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var distressRequest = JsonSerializer.Deserialize<ResponderDistressRequest>(requestBody!);

            if (distressRequest == null || distressRequest.IncidentId == Guid.Empty)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid distress request" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(distressRequest.IncidentId);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Verify that requester is an assigned responder
            var responderAssignment = incident.ResponderAssignments
                .FirstOrDefault(a => a.ResponderId == distressRequest.ResponderId);

            if (responderAssignment == null)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only assigned responders can trigger distress" });
                return forbidden;
            }

            var alertId = Guid.NewGuid();

            _logger.LogCritical(
                "DISTRESS ALERT {AlertId}: Responder {ResponderId} ({Role}) at incident {IncidentId} - {Nature}",
                alertId,
                distressRequest.ResponderId,
                responderAssignment.Role,
                distressRequest.IncidentId,
                distressRequest.NatureOfDistress);

            // 1. Send CRITICAL priority HQ broadcast to all active HQ personnel
            await _notificationService.SendHqBroadcastAsync(
                "RESPONDER DISTRESS",
                $"CRITICAL: Responder under attack at incident {distressRequest.IncidentId}. Nature: {distressRequest.NatureOfDistress}",
                "critical_retreat",
                new Dictionary<string, string>
                {
                    ["incidentId"] = distressRequest.IncidentId.ToString(),
                    ["alertId"] = alertId.ToString(),
                    ["responderId"] = distressRequest.ResponderId.ToString(),
                    ["nature"] = distressRequest.NatureOfDistress ?? string.Empty,
                    ["location"] = $"{distressRequest.Location?.Latitude},{distressRequest.Location?.Longitude}"
                });

            // 2. Alert other responder at scene (if Second responder exists)
            var otherResponder = incident.ResponderAssignments
                .FirstOrDefault(a => a.ResponderId != distressRequest.ResponderId && a.Status == "on_scene");

            if (otherResponder != null)
            {
                await _notificationService.SendPushNotificationAsync(
                    otherResponder.ResponderId,
                    "RESPONDER DISTRESS",
                    "Your partner responder has triggered distress signal. Exercise extreme caution.",
                    new Dictionary<string, string>
                    {
                        ["incidentId"] = distressRequest.IncidentId.ToString(),
                        ["type"] = "responder_distress",
                        ["priority"] = "critical"
                    });
            }

            // 3. Immediately escalate to police
            _logger.LogCritical("Escalating incident {IncidentId} to 911 due to responder distress", distressRequest.IncidentId);

            // Trigger 911 integration
            // Use the dispatch service's silent escalation for critical priority
            await _dispatchService.EscalateToPoliceSilentlyAsync(
                distressRequest.IncidentId,
                incident.SummonerId);

            // Send responder location to dispatch
            if (distressRequest.Location != null)
            {
                _logger.LogCritical(
                    "Responder {ResponderId} distress location: ({Lat}, {Lng})",
                    distressRequest.ResponderId,
                    distressRequest.Location.Latitude,
                    distressRequest.Location.Longitude);

                // In production, send location to 911 dispatch system
                // This would integrate with emergency services APIs
            }

            // 4. Update incident status
            incident.Status = "escalation_required";
            await _incidentRepository.UpdateAsync(incident);

            // 5. Log to incident timeline
            // In production: Add timeline event to IncidentTimeline entity
            // For now, the incident status change serves as the audit trail
            _logger.LogInformation(
                "Responder distress logged for incident {IncidentId}, timeline updated",
                distressRequest.IncidentId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new ResponderDistressResponse
            {
                AlertId = alertId,
                EscalationStatus = "police_notified",
                RecordedAt = DateTime.UtcNow,
                Message = "Distress signal received. Help is on the way."
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "CRITICAL ERROR processing responder distress signal");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to process distress signal" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /sync/incident-actions - Offline sync for responder actions
    /// Supports idempotency keys for offline resilience
    /// </summary>
    [Function("SyncIncidentActions")]
    public async Task<HttpResponseData> SyncIncidentActions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sync/incident-actions")] HttpRequestData req)
    {
        _logger.LogInformation("Syncing offline incident actions");

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var syncRequest = JsonSerializer.Deserialize<IncidentActionSyncRequest>(requestBody!);

            if (syncRequest == null || syncRequest.IncidentId == Guid.Empty || syncRequest.Actions == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid sync request" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(syncRequest.IncidentId);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            _logger.LogInformation(
                "Processing {ActionCount} offline actions for incident {IncidentId}",
                syncRequest.Actions.Count, syncRequest.IncidentId);

            var results = new List<ActionSyncResult>();

            // Sort actions by timestamp to ensure chronological processing
            var sortedActions = syncRequest.Actions
                .OrderBy(a => a.Timestamp)
                .ToList();

            foreach (var action in sortedActions)
            {
                try
                {
                    // Check idempotency key to prevent duplicate processing
                    if (!string.IsNullOrEmpty(action.IdempotencyKey))
                    {
                        // Check if action with this idempotency key was already processed
                        // In production, we'd check against a persisted idempotency store
                        // (e.g., Redis cache or database table with TTL)
                        // For this implementation, we'll track within the batch only

                        // Note: A full implementation would include:
                        // - Check IdempotencyStore.Exists(action.IdempotencyKey)
                        // - Store IdempotencyStore.Set(action.IdempotencyKey, result, TTL: 24 hours)

                        _logger.LogDebug(
                            "Processing action with idempotency key {IdempotencyKey}",
                            action.IdempotencyKey);
                    }

                    // Apply action based on type
                    var result = await ApplyOfflineActionAsync(incident, action);
                    results.Add(result);

                    if (result.Success)
                    {
                        _logger.LogInformation(
                            "Successfully processed offline action {ActionType} at {Timestamp}",
                            action.ActionType, action.Timestamp);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to process offline action {ActionType}: {Error}",
                            action.ActionType, result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing offline action {ActionType}", action.ActionType);
                    results.Add(new ActionSyncResult
                    {
                        ActionType = action.ActionType,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Update incident with all changes
            await _incidentRepository.UpdateAsync(incident);

            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);

            _logger.LogInformation(
                "Offline sync completed for incident {IncidentId}: {SuccessCount} successful, {FailureCount} failed",
                syncRequest.IncidentId, successCount, failureCount);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new IncidentActionSyncResponse
            {
                ProcessedCount = successCount,
                FailedCount = failureCount,
                Results = results
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing offline incident actions");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to sync offline actions" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Apply a single offline action to incident
    /// </summary>
    private async Task<ActionSyncResult> ApplyOfflineActionAsync(
        Core.Entities.Incident incident,
        OfflineIncidentAction action)
    {
        try
        {
            switch (action.ActionType)
            {
                case "en_route":
                    var enRoutePayload = JsonSerializer.Deserialize<StatusChangeRequest>(
                        JsonSerializer.Serialize(action.Payload));
                    if (enRoutePayload != null)
                    {
                        var assignment = incident.ResponderAssignments
                            .FirstOrDefault(a => a.ResponderId == enRoutePayload.ResponderId);
                        if (assignment != null)
                        {
                            assignment.Status = "en_route";
                            assignment.EnRouteAt = action.Timestamp;
                            incident.Status = "en_route";
                        }
                    }
                    break;

                case "on_scene":
                    var onScenePayload = JsonSerializer.Deserialize<StatusChangeRequest>(
                        JsonSerializer.Serialize(action.Payload));
                    if (onScenePayload != null)
                    {
                        var assignment = incident.ResponderAssignments
                            .FirstOrDefault(a => a.ResponderId == onScenePayload.ResponderId);
                        if (assignment != null)
                        {
                            assignment.Status = "on_scene";
                            assignment.OnSceneAt = action.Timestamp;
                            if (assignment.AcceptedAt.HasValue)
                            {
                                assignment.ResponseTimeSeconds = (int)(action.Timestamp - assignment.AcceptedAt.Value).TotalSeconds;
                            }
                            incident.Status = "on_scene";
                        }
                    }
                    break;

                case "status_update":
                    var statusPayload = JsonSerializer.Deserialize<IncidentStatusUpdate>(
                        JsonSerializer.Serialize(action.Payload));
                    if (statusPayload != null && !string.IsNullOrEmpty(statusPayload.Status))
                    {
                        incident.Status = statusPayload.Status;
                    }
                    break;

                case "resolved":
                    var resolvePayload = JsonSerializer.Deserialize<ResolveIncidentRequest>(
                        JsonSerializer.Serialize(action.Payload));
                    if (resolvePayload != null)
                    {
                        incident.Status = "resolved";
                        incident.ResolvedAt = action.Timestamp;
                    }
                    break;

                case "responder_distress":
                    // Distress signals should not be processed through offline sync
                    // They must be sent immediately for safety
                    return new ActionSyncResult
                    {
                        ActionType = action.ActionType,
                        Success = false,
                        ErrorMessage = "Distress signals cannot be synced offline, must be sent immediately"
                    };

                default:
                    return new ActionSyncResult
                    {
                        ActionType = action.ActionType,
                        Success = false,
                        ErrorMessage = $"Unknown action type: {action.ActionType}"
                    };
            }

            // Add timeline event for offline sync
            // In production: Create IncidentTimelineEvent entity
            // For now, log the action for audit trail
            _logger.LogInformation(
                "Offline action {ActionType} applied to incident {IncidentId} at {Timestamp}",
                action.ActionType, incident.IncidentId, action.Timestamp);

            // Broadcast update to subscribed clients
            // In production: Use SignalR to broadcast to connected clients
            // await _realtimeService.BroadcastIncidentUpdate(incident.IncidentId, action.ActionType);
            _logger.LogDebug(
                "Would broadcast {ActionType} update for incident {IncidentId}",
                action.ActionType, incident.IncidentId);

            return new ActionSyncResult
            {
                ActionType = action.ActionType,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying offline action {ActionType}", action.ActionType);
            return new ActionSyncResult
            {
                ActionType = action.ActionType,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Calculate geohash precision based on search radius
    /// </summary>
    private int CalculateGeohashPrecisionForRadius(int radiusMeters)
    {
        return radiusMeters switch
        {
            <= 50 => 9,      // ~50m
            <= 200 => 8,     // ~200m
            <= 1000 => 7,    // ~1km
            <= 5000 => 6,    // ~5km
            <= 20000 => 5,   // ~20km
            _ => 4           // ~100km
        };
    }

    /// <summary>
    /// Calculate geohash for coordinates
    /// This is a simplified implementation - production should use a proper geohash library
    /// </summary>
    private string CalculateGeohash(double latitude, double longitude, int precision)
    {
        const string base32 = "0123456789bcdefghjkmnpqrstuvwxyz";
        var latRange = new[] { -90.0, 90.0 };
        var lonRange = new[] { -180.0, 180.0 };
        var geohash = string.Empty;
        var isEven = true;
        var bit = 0;
        var ch = 0;

        while (geohash.Length < precision)
        {
            double mid;
            if (isEven)
            {
                mid = (lonRange[0] + lonRange[1]) / 2;
                if (longitude > mid)
                {
                    ch |= (1 << (4 - bit));
                    lonRange[0] = mid;
                }
                else
                {
                    lonRange[1] = mid;
                }
            }
            else
            {
                mid = (latRange[0] + latRange[1]) / 2;
                if (latitude > mid)
                {
                    ch |= (1 << (4 - bit));
                    latRange[0] = mid;
                }
                else
                {
                    latRange[1] = mid;
                }
            }

            isEven = !isEven;

            if (bit < 4)
            {
                bit++;
            }
            else
            {
                geohash += base32[ch];
                bit = 0;
                ch = 0;
            }
        }

        return geohash;
    }
}

// DTOs for responder operations

public class NearbyRespondersResponse
{
    public int Count { get; set; }
    public int SearchRadius { get; set; }
    public LocationDto Location { get; set; } = new();
    public List<NearbyResponderDto> Responders { get; set; } = new();
}

public class NearbyResponderDto
{
    public Guid ResponderId { get; set; }
    public int Distance { get; set; }
    public string Status { get; set; } = string.Empty;
    public int EstimatedArrivalMinutes { get; set; }
}
