using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for Emergency Response Core API.
/// Implements endpoints from emergency-response-core-api.yaml.
/// </summary>
public class EmergencyResponseCoreFunctions
{
    private readonly ILogger<EmergencyResponseCoreFunctions> _logger;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDispatchService _dispatchService;
    private readonly INotificationService _notificationService;
    private readonly IJwtService _jwtService;
    private readonly GeohashService _geohashService = new();

    public EmergencyResponseCoreFunctions(
        ILogger<EmergencyResponseCoreFunctions> logger,
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IDispatchService dispatchService,
        INotificationService notificationService,
        IJwtService jwtService)
    {
        _logger = logger;
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _dispatchService = dispatchService;
        _notificationService = notificationService;
        _jwtService = jwtService;
    }

    /// <summary>
    /// POST /incidents - Report emergency incident
    /// </summary>
    [Function("ReportIncident")]
    public async Task<HttpResponseData> ReportIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var body = await JsonSerializer.DeserializeAsync<IncidentReportRequest>(req.Body);
            if (body == null || body.Location == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Incident details and location are required");
            }

            // Check for idempotency key
            var idempotencyKey = GetIdempotencyKey(req);

            // Calculate geohash for location
            var geohash = _geohashService.Encode(body.Location.Latitude, body.Location.Longitude, 9);

            // Create incident
            var incident = new Incident
            {
                IncidentId = Guid.NewGuid(),
                SummonerId = userId.Value,
                Status = "reported",
                IncidentType = body.IncidentType ?? "other",
                LocationLat = body.Location.Latitude,
                LocationLng = body.Location.Longitude,
                LocationGeohash = geohash,
                Description = body.Description,
                IsMedicalEmergency = body.Severity == "life_threatening" || body.Severity == "critical",
                ReportedAt = DateTime.UtcNow
            };

            await _incidentRepository.CreateAsync(incident);

            _logger.LogInformation(
                "Incident {IncidentId} reported by user at location (geohash: {Geohash})",
                incident.IncidentId,
                geohash.Substring(0, 3) + "***" // Log partial geohash only for PII protection
            );

            // Initiate dispatch
            await _dispatchService.DispatchRespondersAsync(
                incident.IncidentId,
                body.Location.Latitude,
                body.Location.Longitude
            );

            // Update incident status
            incident.Status = "dispatching";
            await _incidentRepository.UpdateAsync(incident);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                incident_id = incident.IncidentId,
                status = incident.Status,
                dispatch_id = Guid.NewGuid(), // TODO: Get actual dispatch ID
                responders_contacted = 0, // TODO: Get actual count
                estimated_response_time = 5 // TODO: Calculate based on responder locations
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting incident");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred reporting incident");
        }
    }

    /// <summary>
    /// GET /incidents - Get incidents by location
    /// </summary>
    [Function("GetIncidents")]
    public async Task<HttpResponseData> GetIncidents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var latStr = query["lat"];
            var lonStr = query["lon"];
            var radiusStr = query["radius"] ?? "1000";
            var activeOnlyStr = query["active_only"] ?? "true";

            if (string.IsNullOrEmpty(latStr) || string.IsNullOrEmpty(lonStr))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Latitude and longitude are required");
            }

            if (!double.TryParse(latStr, out var latitude) || !double.TryParse(lonStr, out var longitude))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Invalid coordinates");
            }

            var radius = double.Parse(radiusStr);
            var activeOnly = bool.Parse(activeOnlyStr);

            // Calculate geohash precision based on radius
            var geohashPrecision = CalculateGeohashPrecisionForRadius(radius);
            var geohash = _geohashService.Encode(latitude, longitude, geohashPrecision);

            // Get incidents by geohash prefix
            var incidents = await _incidentRepository.GetActiveIncidentsByGeohashAsync(geohash);

            var incidentSummaries = incidents
                .Where(i => !activeOnly || (i.Status != "resolved" && i.Status != "cancelled"))
                .Select(i => new
                {
                    incident_id = i.IncidentId,
                    title = "Emergency Incident", // Don't expose details
                    incident_type = i.IncidentType,
                    severity = i.IsMedicalEmergency ? "critical" : "medium",
                    location = new
                    {
                        latitude = i.LocationLat,
                        longitude = i.LocationLng
                    },
                    time_reported = i.ReportedAt,
                    status = i.Status,
                    distance_to_user = (i.LocationLat.HasValue && i.LocationLng.HasValue) ? CalculateDistance(latitude, longitude, i.LocationLat.Value, i.LocationLng.Value) : (double?)null
                })
                .OrderBy(i => i.distance_to_user)
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(incidentSummaries);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving incidents");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving incidents");
        }
    }

    /// <summary>
    /// POST /responders/register - Register as emergency responder
    /// </summary>
    [Function("RegisterResponder")]
    public async Task<HttpResponseData> RegisterResponder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/register")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var body = await JsonSerializer.DeserializeAsync<ResponderRegistrationRequest>(req.Body);
            if (body == null || body.Qualifications == null || !body.Qualifications.Any())
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "At least one qualification is required");
            }

            var user = await _userRepository.GetByIdAsync(userId.Value);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "USER_NOT_FOUND", "User not found");
            }

            // Update user to responder-eligible status
            user.AccountType = "responder_eligible";
            await _userRepository.UpdateAsync(user);

            // TODO: Create ResponderProfile entity with qualifications, certifications, etc.

            _logger.LogInformation("User {UserId} registered as responder", userId.Value);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                responder_id = userId.Value.ToString(),
                status = "active",
                verified = false,
                reputation_score = 0.0,
                total_responses = 0
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering responder");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred registering responder");
        }
    }

    /// <summary>
    /// PUT /responders/availability - Update responder availability
    /// </summary>
    [Function("UpdateResponderAvailability")]
    public async Task<HttpResponseData> UpdateResponderAvailability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "responders/availability")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var body = await JsonSerializer.DeserializeAsync<ResponderAvailabilityUpdateRequest>(req.Body);
            if (body == null || body.CurrentLocation == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Status and current location are required");
            }

            // TODO: Update responder profile with availability status and location
            // This would typically involve updating a ResponderProfile entity

            _logger.LogInformation(
                "Responder availability updated (location not logged for PII protection)"
            );

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                responder_id = userId.Value.ToString(),
                status = body.Status,
                verified = true,
                reputation_score = 4.5,
                total_responses = 0
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating responder availability");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred updating availability");
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/respond - Respond to incident as volunteer
    /// </summary>
    [Function("RespondToIncident")]
    public async Task<HttpResponseData> RespondToIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/respond")] HttpRequestData req,
        string incidentId)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_INCIDENT_ID", "Invalid incident ID");
            }

            var body = await JsonSerializer.DeserializeAsync<IncidentResponseRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Response details are required");
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "INCIDENT_NOT_FOUND", "Incident not found");
            }

            // Create responder assignment
            var assignment = new ResponderAssignment
            {
                AssignmentId = Guid.NewGuid(),
                IncidentId = incidentGuid,
                ResponderId = userId.Value,
                Role = "First", // TODO: Determine role based on acceptance order
                Status = "assigned",
                AssignedAt = DateTime.UtcNow,
                AcceptedAt = DateTime.UtcNow
            };

            // TODO: Save assignment to database via repository

            _logger.LogInformation(
                "Responder accepted incident {IncidentId} with ETA {ETA} minutes",
                incidentGuid,
                body.EstimatedArrival
            );

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                assignment_id = assignment.AssignmentId,
                incident_id = incidentGuid,
                responder_id = userId.Value.ToString(),
                status = "assigned",
                assigned_at = assignment.AssignedAt,
                estimated_arrival = body.EstimatedArrival
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error responding to incident");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred responding to incident");
        }
    }

    /// <summary>
    /// POST /dispatch/alert - Create emergency dispatch
    /// </summary>
    [Function("CreateEmergencyDispatch")]
    public async Task<HttpResponseData> CreateEmergencyDispatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dispatch/alert")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var body = await JsonSerializer.DeserializeAsync<EmergencyDispatchRequest>(req.Body);
            if (body == null || body.IncidentId == Guid.Empty || body.Location == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Incident ID and location are required");
            }

            var incident = await _incidentRepository.GetByIdAsync(body.IncidentId);
            if (incident == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "INCIDENT_NOT_FOUND", "Incident not found");
            }

            // Calculate geohash for initial dispatch ring
            var geohashPrecision = body.InitialGeohashPrecision ?? 9;
            var geohash = _geohashService.Encode(body.Location.Latitude, body.Location.Longitude, geohashPrecision);

            // Find nearby responders
            var nearbyResponders = await _dispatchService.FindNearbyRespondersAsync(
                geohash,
                body.ResponseThreshold ?? 5
            );

            // Send notifications
            foreach (var responderId in nearbyResponders)
            {
                await _notificationService.SendDispatchNotificationAsync(
                    responderId,
                    body.IncidentId,
                    body.EmergencyType,
                    body.Severity ?? "medium"
                );
            }

            _logger.LogInformation(
                "Emergency dispatch created for incident {IncidentId}, contacted {Count} responders",
                body.IncidentId,
                nearbyResponders.Count()
            );

            var dispatchId = Guid.NewGuid();
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                dispatch_id = dispatchId,
                initial_contacts = nearbyResponders.Count(),
                responders_in_radius = nearbyResponders.Count(),
                estimated_first_response = 5 // Minutes - TODO: Calculate based on responder distances
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating emergency dispatch");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred creating dispatch");
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/checkin - Responder indicates they found summoning user
    /// </summary>
    [Function("ResponderCheckin")]
    public async Task<HttpResponseData> ResponderCheckin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/checkin")] HttpRequestData req,
        string incidentId)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_INCIDENT_ID", "Invalid incident ID");
            }

            var body = await JsonSerializer.DeserializeAsync<ResponderCheckinRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Check-in details are required");
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "INCIDENT_NOT_FOUND", "Incident not found");
            }

            // Update incident status
            incident.Status = "on_scene";
            await _incidentRepository.UpdateAsync(incident);

            // TODO: Create check-in record with assessment details

            // Notify summoner that responder has checked in
            await _notificationService.SendIncidentNotificationAsync(
                incident.SummonerId,
                incidentGuid,
                "responder_arrived",
                "A responder has arrived at your location"
            );

            _logger.LogInformation(
                "Responder checked in for incident {IncidentId}",
                incidentGuid
            );

            var checkinId = Guid.NewGuid();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                checkin_id = checkinId,
                checkin_time = DateTime.UtcNow,
                user_notification_sent = true,
                next_phase = "awaiting_user_confirmation"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing responder check-in");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during check-in");
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/user-confirm - User confirms responder contact
    /// </summary>
    [Function("UserConfirmContact")]
    public async Task<HttpResponseData> UserConfirmContact(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/user-confirm")] HttpRequestData req,
        string incidentId)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_INCIDENT_ID", "Invalid incident ID");
            }

            var body = await JsonSerializer.DeserializeAsync<UserConfirmRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Confirmation details are required");
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "INCIDENT_NOT_FOUND", "Incident not found");
            }

            // Verify user is the summoner
            if (incident.SummonerId != userId.Value)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "FORBIDDEN", "Only the summoner can confirm contact");
            }

            // Update incident status
            if (body.ThreatResolved && body.FeedsSafe)
            {
                incident.Status = "de_escalating";
            }

            await _incidentRepository.UpdateAsync(incident);

            _logger.LogInformation(
                "User confirmed contact for incident {IncidentId}",
                incidentGuid
            );

            var confirmationId = Guid.NewGuid();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                confirmation_id = confirmationId,
                confirmation_time = DateTime.UtcNow,
                safety_clock_started = body.ThreatResolved,
                estimated_safety_decision_time = 300 // 5 minutes
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user confirmation");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during confirmation");
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/safe - Mark situation as safe
    /// </summary>
    [Function("MarkIncidentSafe")]
    public async Task<HttpResponseData> MarkIncidentSafe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/safe")] HttpRequestData req,
        string incidentId)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_INCIDENT_ID", "Invalid incident ID");
            }

            var body = await JsonSerializer.DeserializeAsync<SafetyConfirmationRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Safety confirmation details are required");
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "INCIDENT_NOT_FOUND", "Incident not found");
            }

            // TODO: Track safety confirmations from both summoner and responders
            // Only mark as resolved when all participants confirm

            if (body.SafetyConfirmed && body.IncidentResolved)
            {
                incident.Status = "resolved";
                incident.ResolvedAt = DateTime.UtcNow;
                await _incidentRepository.UpdateAsync(incident);

                _logger.LogInformation(
                    "Incident {IncidentId} marked as safe and resolved",
                    incidentGuid
                );
            }

            var confirmationId = Guid.NewGuid();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                confirmation_id = confirmationId,
                all_confirmations_received = body.SafetyConfirmed,
                incident_closure_authorized = body.IncidentResolved
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking incident safe");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred marking incident safe");
        }
    }

    // Helper methods

    private async Task<Guid?> ExtractUserIdFromToken(HttpRequestData req)
    {
        try
        {
            if (!req.Headers.TryGetValues("Authorization", out var authHeaders))
            {
                return null;
            }

            var authHeader = authHeaders.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return null;
            }

            var token = authHeader.Substring("Bearer ".Length);
            return await _jwtService.ValidateAccessTokenAsync(token);
        }
        catch
        {
            return null;
        }
    }

    private string? GetIdempotencyKey(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("Idempotency-Key", out var values))
        {
            return values.FirstOrDefault();
        }
        return null;
    }

    private int CalculateGeohashPrecisionForRadius(double radiusMeters)
    {
        // Geohash precision to radius mapping (approximate)
        // 9 = ~5m, 8 = ~40m, 7 = ~150m, 6 = ~1.2km, 5 = ~5km
        if (radiusMeters <= 5) return 9;
        if (radiusMeters <= 40) return 8;
        if (radiusMeters <= 150) return 7;
        if (radiusMeters <= 1200) return 6;
        return 5;
    }

    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        // Haversine formula for calculating distance between two points
        const double R = 6371000; // Earth's radius in meters
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string code, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new
        {
            code,
            message
        });
        return response;
    }

    // Request DTOs

    private class IncidentReportRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? IncidentType { get; set; }
        public string? Severity { get; set; }
        public LocationDto? Location { get; set; }
        public DateTime? OccurredAt { get; set; }
    }

    private class LocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Accuracy { get; set; }
    }

    private class ResponderRegistrationRequest
    {
        public string[]? Qualifications { get; set; }
        public string[]? Certifications { get; set; }
        public double? ResponseRadius { get; set; }
        public string[]? ResponseTypes { get; set; }
    }

    private class ResponderAvailabilityUpdateRequest
    {
        public string Status { get; set; } = string.Empty;
        public LocationDto? CurrentLocation { get; set; }
        public DateTime? AvailableUntil { get; set; }
    }

    private class IncidentResponseRequest
    {
        public string? ResponseType { get; set; }
        public int EstimatedArrival { get; set; }
        public string[]? Capabilities { get; set; }
        public string? Message { get; set; }
    }

    private class EmergencyDispatchRequest
    {
        public Guid IncidentId { get; set; }
        public string EmergencyType { get; set; } = string.Empty;
        public LocationDto? Location { get; set; }
        public string? Severity { get; set; }
        public int? ResponseThreshold { get; set; }
        public int? InitialGeohashPrecision { get; set; }
        public int? ExpansionTimeWindow { get; set; }
        public int? MaxExpansionRings { get; set; }
        public bool AutoExpand { get; set; } = true;
    }

    private class ResponderCheckinRequest
    {
        public string ResponderId { get; set; } = string.Empty;
        public bool ContactConfirmed { get; set; }
        public LocationDto? Location { get; set; }
        public UserConditionAssessment? UserConditionAssessment { get; set; }
    }

    private class UserConditionAssessment
    {
        public bool AppearsSafe { get; set; }
        public bool MedicalAttentionNeeded { get; set; }
        public bool ImmediateThreat { get; set; }
    }

    private class UserConfirmRequest
    {
        public bool ContactConfirmed { get; set; }
        public bool FeedsSafe { get; set; }
        public bool ThreatResolved { get; set; }
    }

    private class SafetyConfirmationRequest
    {
        public string ParticipantType { get; set; } = string.Empty;
        public bool SafetyConfirmed { get; set; }
        public bool IncidentResolved { get; set; }
        public bool NoOngoingThreat { get; set; }
    }
}
