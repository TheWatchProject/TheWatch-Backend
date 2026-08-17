using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for incident management operations.
/// Implements endpoints from incident-checkin-api.yaml
/// </summary>
public class IncidentFunctions
{
    private readonly ILogger<IncidentFunctions> _logger;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IDispatchService _dispatchService;
    private readonly INotificationService _notificationService;
    private readonly IRealtimeService _realtimeService;
    private readonly GeohashService _geohashService;
    private readonly Dictionary<string, string> _processedIdempotencyKeys = new();

    public IncidentFunctions(
        ILogger<IncidentFunctions> logger,
        IIncidentRepository incidentRepository,
        IDispatchService dispatchService,
        INotificationService notificationService,
        IRealtimeService realtimeService)
    {
        _logger = logger;
        _incidentRepository = incidentRepository;
        _dispatchService = dispatchService;
        _notificationService = notificationService;
        _realtimeService = realtimeService;
        _geohashService = new GeohashService();
    }

    /// <summary>
    /// POST /incidents - Create a new incident (from incident-detection-api.yaml)
    /// </summary>
    [Function("CreateIncident")]
    public async Task<HttpResponseData> CreateIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents")] HttpRequestData req)
    {
        _logger.LogInformation("Creating new incident");

        try
        {
            // 1. Parse request body
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request body is required" });
                return badRequest;
            }

            var createRequest = JsonSerializer.Deserialize<CreateIncidentRequest>(requestBody);
            if (createRequest == null || !createRequest.Latitude.HasValue || !createRequest.Longitude.HasValue)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Valid location coordinates are required" });
                return badRequest;
            }

            // 2. Validate summoner authentication
            var summonerId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!summonerId.HasValue)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Valid authentication token is required" });
                return unauthorized;
            }

            // 3. Calculate geohash from location
            var geohash = _geohashService.Encode(
                createRequest.Latitude.Value,
                createRequest.Longitude.Value,
                9);

            _logger.LogInformation(
                "Creating incident for summoner {SummonerId} at ({Lat}, {Lng}), geohash: {Geohash}",
                summonerId.Value, createRequest.Latitude.Value, createRequest.Longitude.Value, geohash);

            // 4. Create incident record
            var incident = new Incident
            {
                IncidentId = Guid.NewGuid(),
                SummonerId = summonerId.Value,
                Status = "dispatch_in_progress",
                IncidentType = createRequest.IncidentType ?? "other",
                LocationLat = createRequest.Latitude.Value,
                LocationLng = createRequest.Longitude.Value,
                LocationGeohash = geohash,
                LocationAddress = createRequest.Address,
                LocationBuilding = createRequest.Building,
                Description = createRequest.Description,
                IsMedicalEmergency = createRequest.IsMedicalEmergency ?? false,
                SummonerSafe = createRequest.SummonerSafe ?? true,
                ReportedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TriggeredPhraseId = createRequest.TriggerPhraseId,
                DuressFlag = false
            };

            await _incidentRepository.CreateAsync(incident);

            _logger.LogInformation(
                "Incident {IncidentId} created successfully for summoner {SummonerId}",
                incident.IncidentId, summonerId.Value);

            // 5. Trigger dispatch orchestrator
            await _dispatchService.DispatchRespondersAsync(
                incident.IncidentId,
                incident.LocationLat.Value,
                incident.LocationLng.Value);

            _logger.LogInformation("Dispatch triggered for incident {IncidentId}", incident.IncidentId);

            // 6. Return incident details
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new CreateIncidentResponse
            {
                IncidentId = incident.IncidentId,
                Status = incident.Status,
                Geohash = incident.LocationGeohash,
                CreatedAt = incident.ReportedAt,
                Message = "Incident created successfully, dispatching responders"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating incident");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to create incident" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /incidents/{incidentId} - Get incident details
    /// </summary>
    [Function("GetIncident")]
    public async Task<HttpResponseData> GetIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incidents/{incidentId}")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Getting incident: {IncidentId}", incidentId);

        try
        {
            // 1. Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Valid authentication token is required" });
                return unauthorized;
            }

            // Parse incident ID
            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID format" });
                return badRequest;
            }

            // 3. Retrieve incident from database
            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // 2. Check authorization (summoner, assigned responder, or HQ)
            var roles = JwtUtilities.ExtractRolesFromToken(req);
            var isHqOrAdmin = roles.Contains("hq", StringComparer.OrdinalIgnoreCase) ||
                              roles.Contains("admin", StringComparer.OrdinalIgnoreCase);
            var isSummoner = incident.SummonerId == userId.Value;
            var isAssignedResponder = incident.ResponderAssignments.Any(a => a.ResponderId == userId.Value);

            if (!isHqOrAdmin && !isSummoner && !isAssignedResponder)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You do not have permission to view this incident" });
                return forbidden;
            }

            _logger.LogInformation(
                "User {UserId} retrieved incident {IncidentId} (Role: {Roles})",
                userId.Value, incidentId, string.Join(",", roles));

            // 4. Return incident details
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new IncidentDetailsDto
            {
                IncidentId = incident.IncidentId,
                SummonerId = incident.SummonerId,
                Status = incident.Status,
                IncidentType = incident.IncidentType,
                Description = incident.Description,
                IsMedicalEmergency = incident.IsMedicalEmergency,
                Location = new LocationDto
                {
                    Latitude = incident.LocationLat,
                    Longitude = incident.LocationLng,
                    Geohash = incident.LocationGeohash,
                    Address = incident.LocationAddress
                },
                CreatedAt = incident.ReportedAt,
                ResolvedAt = incident.ResolvedAt,
                ResponderAssignments = incident.ResponderAssignments.Select(a => new ResponderRoleDto
                {
                    AssignmentId = a.AssignmentId,
                    IncidentId = a.IncidentId,
                    ResponderId = a.ResponderId,
                    Role = a.Role,
                    Status = a.Status,
                    AssignedAt = a.AssignedAt,
                    ResponseTimeSeconds = a.ResponseTimeSeconds
                }).ToList()
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving incident {IncidentId}", incidentId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve incident" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/accept - Responder accepts dispatch
    /// </summary>
    [Function("AcceptDispatch")]
    public async Task<HttpResponseData> AcceptDispatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/accept")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Responder accepting dispatch for incident: {IncidentId}", incidentId);

        try
        {
            // Check idempotency
            var idempotencyKey = req.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null;
            if (!string.IsNullOrEmpty(idempotencyKey) && _processedIdempotencyKeys.ContainsKey(idempotencyKey))
            {
                var cachedResponse = req.CreateResponse(HttpStatusCode.OK);
                await cachedResponse.WriteAsJsonAsync(new { message = "Request already processed (idempotent)" });
                return cachedResponse;
            }

            // Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Valid authentication token is required" });
                return unauthorized;
            }

            // Parse request
            var requestBody = await req.ReadAsStringAsync();
            var acceptRequest = JsonSerializer.Deserialize<AcceptDispatchRequest>(requestBody ?? "{}");
            if (acceptRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequest;
            }

            // Validate incident
            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID format" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Validate incident state
            if (incident.Status == "resolved")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Incident is already resolved" });
                return conflict;
            }

            // Check if responder already assigned
            var existingAssignment = incident.ResponderAssignments.FirstOrDefault(a => a.ResponderId == userId.Value);
            if (existingAssignment != null)
            {
                if (existingAssignment.Status == "declined")
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "You have already declined this incident" });
                    return conflict;
                }

                // Already accepted - return existing assignment
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new ResponderRoleDto
                {
                    AssignmentId = existingAssignment.AssignmentId,
                    IncidentId = existingAssignment.IncidentId,
                    ResponderId = existingAssignment.ResponderId,
                    Role = existingAssignment.Role,
                    Status = existingAssignment.Status,
                    AssignedAt = existingAssignment.AssignedAt,
                    ResponseTimeSeconds = existingAssignment.ResponseTimeSeconds
                });
                return response;
            }

            // Check if incident already has 2 responders
            var activeResponders = incident.ResponderAssignments.Count(a => a.Status != "declined");
            if (activeResponders >= 2)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Incident already has maximum number of responders" });
                return conflict;
            }

            // Assign role (First or Second)
            var role = activeResponders == 0 ? "First" : "Second";
            var assignment = new ResponderAssignment
            {
                AssignmentId = Guid.NewGuid(),
                IncidentId = incidentGuid,
                ResponderId = userId.Value,
                Role = role,
                Status = "assigned",
                AssignedAt = DateTime.UtcNow,
                AcceptedAt = DateTime.UtcNow,
                ResponseTimeSeconds = (int)(DateTime.UtcNow - incident.ReportedAt).TotalSeconds
            };

            incident.ResponderAssignments.Add(assignment);
            incident.Status = "awaiting_response";
            incident.UpdatedAt = DateTime.UtcNow;

            await _incidentRepository.UpdateAsync(incident);

            // Store idempotency key
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _processedIdempotencyKeys[idempotencyKey] = incidentId;
            }

            // Send notifications
            await _notificationService.SendIncidentNotificationAsync(
                incident.SummonerId,
                incidentGuid,
                "responder_accepted",
                $"A {role} responder has accepted your incident");

            // Broadcast real-time update
            await _realtimeService.BroadcastIncidentEventAsync(
                incidentGuid,
                "responder_accepted",
                new { responderId = userId.Value, role, estimatedArrival = acceptRequest.EstimatedArrivalMinutes });

            _logger.LogInformation(
                "Responder {ResponderId} accepted incident {IncidentId} as {Role}",
                userId.Value, incidentId, role);

            var successResponse = req.CreateResponse(HttpStatusCode.OK);
            await successResponse.WriteAsJsonAsync(new ResponderRoleDto
            {
                AssignmentId = assignment.AssignmentId,
                IncidentId = assignment.IncidentId,
                ResponderId = assignment.ResponderId,
                Role = assignment.Role,
                Status = assignment.Status,
                AssignedAt = assignment.AssignedAt,
                ResponseTimeSeconds = assignment.ResponseTimeSeconds
            });

            return successResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting dispatch for incident {IncidentId}", incidentId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to accept dispatch" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/decline - Responder declines dispatch
    /// </summary>
    [Function("DeclineDispatch")]
    public async Task<HttpResponseData> DeclineDispatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/decline")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Responder declining dispatch for incident: {IncidentId}", incidentId);

        try
        {
            // Check idempotency
            var idempotencyKey = req.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null;
            if (!string.IsNullOrEmpty(idempotencyKey) && _processedIdempotencyKeys.ContainsKey(idempotencyKey))
            {
                var cachedResponse = req.CreateResponse(HttpStatusCode.NoContent);
                return cachedResponse;
            }

            // Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Valid authentication token is required" });
                return unauthorized;
            }

            // Parse request
            var requestBody = await req.ReadAsStringAsync();
            var declineRequest = JsonSerializer.Deserialize<DeclineDispatchRequest>(requestBody ?? "{}");

            // Validate incident
            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID format" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Find responder assignment
            var assignment = incident.ResponderAssignments.FirstOrDefault(a => a.ResponderId == userId.Value);
            if (assignment != null)
            {
                // Check if already responded
                if (assignment.Status != "assigned" && assignment.Status != "declined")
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "Cannot decline - you have already responded to this incident" });
                    return conflict;
                }

                assignment.Status = "declined";
                assignment.DeclinedAt = DateTime.UtcNow;
                incident.UpdatedAt = DateTime.UtcNow;

                await _incidentRepository.UpdateAsync(incident);
            }

            // Store idempotency key
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _processedIdempotencyKeys[idempotencyKey] = incidentId;
            }

            // Continue dispatch to next responders if needed
            var activeResponders = incident.ResponderAssignments.Count(a => a.Status != "declined");
            if (activeResponders < 2)
            {
                await _dispatchService.DispatchRespondersAsync(
                    incidentGuid,
                    incident.LocationLat!.Value,
                    incident.LocationLng!.Value);
            }

            // Broadcast real-time update
            await _realtimeService.BroadcastIncidentEventAsync(
                incidentGuid,
                "responder_declined",
                new { responderId = userId.Value, reason = declineRequest?.Reason });

            _logger.LogInformation(
                "Responder {ResponderId} declined incident {IncidentId}. Reason: {Reason}",
                userId.Value, incidentId, declineRequest?.Reason ?? "Not specified");

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error declining dispatch for incident {IncidentId}", incidentId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to decline dispatch" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/en-route - Responder marks en-route
    /// </summary>
    [Function("MarkEnRoute")]
    public async Task<HttpResponseData> MarkEnRoute(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/en-route")] HttpRequestData req,
        string incidentId)
    {
        return await UpdateIncidentStatusAsync(req, incidentId, "en_route", "en-route");
    }

    /// <summary>
    /// POST /incidents/{incidentId}/on-scene - Responder marks on-scene
    /// </summary>
    [Function("MarkOnScene")]
    public async Task<HttpResponseData> MarkOnScene(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/on-scene")] HttpRequestData req,
        string incidentId)
    {
        return await UpdateIncidentStatusAsync(req, incidentId, "on_scene", "on-scene");
    }

    /// <summary>
    /// POST /incidents/{incidentId}/resolved - Mark incident resolved (First responder only)
    /// </summary>
    [Function("ResolveIncident")]
    public async Task<HttpResponseData> ResolveIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/resolved")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Resolving incident: {IncidentId}", incidentId);

        try
        {
            // Check idempotency
            var idempotencyKey = req.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null;
            if (!string.IsNullOrEmpty(idempotencyKey) && _processedIdempotencyKeys.ContainsKey(idempotencyKey))
            {
                var cachedResponse = req.CreateResponse(HttpStatusCode.OK);
                await cachedResponse.WriteAsJsonAsync(new { message = "Request already processed (idempotent)" });
                return cachedResponse;
            }

            // Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Valid authentication token is required" });
                return unauthorized;
            }

            // Parse request
            var requestBody = await req.ReadAsStringAsync();
            var resolveRequest = JsonSerializer.Deserialize<ResolveIncidentRequest>(requestBody ?? "{}");

            // Validate incident
            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID format" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Validate state transition
            if (!IsValidStatusTransition(incident.Status, "resolved"))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = $"Cannot resolve incident from status '{incident.Status}'" });
                return conflict;
            }

            // Only First responder can resolve
            var firstResponder = incident.ResponderAssignments.FirstOrDefault(a => a.Role == "First");
            if (firstResponder == null || firstResponder.ResponderId != userId.Value)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only First responder can resolve incident" });
                return forbidden;
            }

            // Update incident status
            incident.Status = "resolved";
            incident.ResolvedAt = DateTime.UtcNow;
            incident.UpdatedAt = DateTime.UtcNow;

            await _incidentRepository.UpdateAsync(incident);

            // Store idempotency key
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _processedIdempotencyKeys[idempotencyKey] = incidentId;
            }

            // Send notifications
            await _notificationService.SendIncidentNotificationAsync(
                incident.SummonerId,
                incidentGuid,
                "incident_resolved",
                "Your incident has been resolved. All clear.");

            // Notify other responders
            foreach (var assignment in incident.ResponderAssignments.Where(a => a.ResponderId != userId.Value))
            {
                await _notificationService.SendIncidentNotificationAsync(
                    assignment.ResponderId,
                    incidentGuid,
                    "incident_resolved",
                    "Incident resolved by First responder");
            }

            // Broadcast real-time update
            await _realtimeService.BroadcastIncidentEventAsync(
                incidentGuid,
                "incident_resolved",
                new
                {
                    responderId = userId.Value,
                    allClear = resolveRequest?.AllClear ?? true,
                    resolutionNotes = resolveRequest?.ResolutionNotes
                });

            _logger.LogInformation(
                "Incident {IncidentId} resolved by responder {ResponderId}. All Clear: {AllClear}",
                incidentId, userId.Value, resolveRequest?.AllClear ?? true);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new IncidentDetailsDto
            {
                IncidentId = incident.IncidentId,
                SummonerId = incident.SummonerId,
                Status = incident.Status,
                IncidentType = incident.IncidentType,
                Description = incident.Description,
                IsMedicalEmergency = incident.IsMedicalEmergency,
                Location = new LocationDto
                {
                    Latitude = incident.LocationLat,
                    Longitude = incident.LocationLng,
                    Geohash = incident.LocationGeohash,
                    Address = incident.LocationAddress
                },
                CreatedAt = incident.ReportedAt,
                ResolvedAt = incident.ResolvedAt,
                ResponderAssignments = incident.ResponderAssignments.Select(a => new ResponderRoleDto
                {
                    AssignmentId = a.AssignmentId,
                    IncidentId = a.IncidentId,
                    ResponderId = a.ResponderId,
                    Role = a.Role,
                    Status = a.Status,
                    AssignedAt = a.AssignedAt,
                    ResponseTimeSeconds = a.ResponseTimeSeconds
                }).ToList()
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving incident {IncidentId}", incidentId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to resolve incident" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/responder-distress - Responder panic button
    /// </summary>
    [Function("ResponderDistress")]
    public async Task<HttpResponseData> ResponderDistress(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/responder-distress")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogCritical("RESPONDER DISTRESS SIGNAL received for incident: {IncidentId}", incidentId);

        try
        {
            // Check idempotency
            var idempotencyKey = req.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null;
            if (!string.IsNullOrEmpty(idempotencyKey) && _processedIdempotencyKeys.ContainsKey(idempotencyKey))
            {
                var cachedResponse = req.CreateResponse(HttpStatusCode.OK);
                await cachedResponse.WriteAsJsonAsync(new { message = "Distress signal already processed (idempotent)" });
                return cachedResponse;
            }

            // Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Valid authentication token is required" });
                return unauthorized;
            }

            // Parse request
            var requestBody = await req.ReadAsStringAsync();
            var distressRequest = JsonSerializer.Deserialize<ResponderDistressRequest>(requestBody ?? "{}");

            // Validate incident
            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID format" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Verify responder is assigned to this incident
            var assignment = incident.ResponderAssignments.FirstOrDefault(a => a.ResponderId == userId.Value);
            if (assignment == null)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only assigned responders may trigger distress for this incident" });
                return forbidden;
            }

            // Escalate to police immediately
            await _dispatchService.EscalateToPoliceSilentlyAsync(incidentGuid, userId.Value);

            // Store idempotency key
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _processedIdempotencyKeys[idempotencyKey] = incidentId;
            }

            // Send high-priority alerts
            await _notificationService.SendResponderDistressAlertAsync(userId.Value, incidentGuid);

            // Notify HQ immediately
            await _notificationService.SendHqBroadcastAsync(
                $"RESPONDER DISTRESS: Responder {userId.Value} triggered panic button for incident {incidentId}",
                incidentGuid);

            // Notify other responders
            foreach (var otherAssignment in incident.ResponderAssignments.Where(a => a.ResponderId != userId.Value))
            {
                await _notificationService.SendIncidentNotificationAsync(
                    otherAssignment.ResponderId,
                    incidentGuid,
                    "responder_distress",
                    "EMERGENCY: Another responder is in distress!");
            }

            // Broadcast real-time update
            await _realtimeService.BroadcastIncidentEventAsync(
                incidentGuid,
                "responder_distress",
                new
                {
                    responderId = userId.Value,
                    nature = distressRequest?.NatureOfDistress,
                    location = distressRequest?.Location,
                    timestamp = distressRequest?.Timestamp ?? DateTime.UtcNow
                });

            _logger.LogCritical(
                "Responder distress processed for incident {IncidentId}, responder {ResponderId}. Nature: {Nature}",
                incidentId, userId.Value, distressRequest?.NatureOfDistress ?? "unknown");

            var alertId = Guid.NewGuid();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new ResponderDistressResponse
            {
                AlertId = alertId,
                EscalationStatus = "police_notified",
                RecordedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing responder distress for incident {IncidentId}", incidentId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to process distress signal" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /sync/incident-actions - Offline sync for responder actions
    /// </summary>
    [Function("SyncIncidentActions")]
    public async Task<HttpResponseData> SyncIncidentActions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sync/incident-actions")] HttpRequestData req)
    {
        _logger.LogInformation("Syncing offline incident actions");

        try
        {
            // 1. Parse batched actions from request
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request body is required" });
                return badRequest;
            }

            var syncRequest = JsonSerializer.Deserialize<IncidentActionSyncRequest>(requestBody);
            if (syncRequest == null || syncRequest.IncidentId == Guid.Empty || syncRequest.Actions == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid sync request" });
                return badRequest;
            }

            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Valid authentication token is required" });
                return unauthorized;
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
            var processedIdempotencyKeys = new HashSet<string>();

            // 3. Apply actions in chronological order
            var sortedActions = syncRequest.Actions
                .OrderBy(a => a.Timestamp)
                .ToList();

            foreach (var action in sortedActions)
            {
                try
                {
                    // 2. Validate idempotency keys
                    if (!string.IsNullOrEmpty(action.IdempotencyKey))
                    {
                        // Check if this action was already processed in this batch
                        if (processedIdempotencyKeys.Contains(action.IdempotencyKey))
                        {
                            _logger.LogInformation(
                                "Skipping duplicate action with idempotency key {IdempotencyKey}",
                                action.IdempotencyKey);

                            results.Add(new ActionSyncResult
                            {
                                ActionType = action.ActionType,
                                Success = true,
                                ErrorMessage = "Already processed (duplicate in batch)"
                            });
                            continue;
                        }

                        processedIdempotencyKeys.Add(action.IdempotencyKey);
                    }

                    // Apply action based on type
                    var result = await ApplyOfflineActionAsync(incident, action, userId.Value);
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

            // 4. Handle conflicts - Update incident with all changes
            await _incidentRepository.UpdateAsync(incident);

            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);

            _logger.LogInformation(
                "Offline sync completed for incident {IncidentId}: {SuccessCount} successful, {FailureCount} failed",
                syncRequest.IncidentId, successCount, failureCount);

            // 5. Return sync results
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
        Incident incident,
        OfflineIncidentAction action,
        Guid userId)
    {
        try
        {
            switch (action.ActionType)
            {
                case "accept":
                    var acceptPayload = JsonSerializer.Deserialize<AcceptDispatchRequest>(
                        JsonSerializer.Serialize(action.Payload));
                    if (acceptPayload != null && acceptPayload.ResponderId == userId)
                    {
                        // Check if already accepted
                        var existingAssignment = incident.ResponderAssignments
                            .FirstOrDefault(a => a.ResponderId == userId);

                        if (existingAssignment == null)
                        {
                            var role = incident.ResponderAssignments.Count(a => a.Status != "declined") == 0
                                ? "First"
                                : "Second";

                            incident.ResponderAssignments.Add(new ResponderAssignment
                            {
                                AssignmentId = Guid.NewGuid(),
                                IncidentId = incident.IncidentId,
                                ResponderId = userId,
                                Role = role,
                                Status = "assigned",
                                AssignedAt = action.Timestamp,
                                AcceptedAt = action.Timestamp
                            });
                        }
                    }
                    break;

                case "en_route":
                    var enRoutePayload = JsonSerializer.Deserialize<StatusChangeRequest>(
                        JsonSerializer.Serialize(action.Payload));
                    if (enRoutePayload != null && enRoutePayload.ResponderId == userId)
                    {
                        var assignment = incident.ResponderAssignments
                            .FirstOrDefault(a => a.ResponderId == userId);
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
                    if (onScenePayload != null && onScenePayload.ResponderId == userId)
                    {
                        var assignment = incident.ResponderAssignments
                            .FirstOrDefault(a => a.ResponderId == userId);
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

                case "resolved":
                    var resolvePayload = JsonSerializer.Deserialize<ResolveIncidentRequest>(
                        JsonSerializer.Serialize(action.Payload));
                    if (resolvePayload != null && resolvePayload.ResponderId == userId)
                    {
                        var firstResponder = incident.ResponderAssignments
                            .FirstOrDefault(a => a.Role == "First");
                        if (firstResponder != null && firstResponder.ResponderId == userId)
                        {
                            incident.Status = "resolved";
                            incident.ResolvedAt = action.Timestamp;
                        }
                        else
                        {
                            return new ActionSyncResult
                            {
                                ActionType = action.ActionType,
                                Success = false,
                                ErrorMessage = "Only First responder can resolve incident"
                            };
                        }
                    }
                    break;

                default:
                    return new ActionSyncResult
                    {
                        ActionType = action.ActionType,
                        Success = false,
                        ErrorMessage = $"Unknown action type: {action.ActionType}"
                    };
            }

            await Task.CompletedTask;

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
    /// Helper method to update incident status for en-route and on-scene
    /// </summary>
    private async Task<HttpResponseData> UpdateIncidentStatusAsync(
        HttpRequestData req,
        string incidentId,
        string newStatus,
        string eventName)
    {
        _logger.LogInformation("Updating incident {IncidentId} status to {Status}", incidentId, newStatus);

        try
        {
            // Check idempotency
            var idempotencyKey = req.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null;
            if (!string.IsNullOrEmpty(idempotencyKey) && _processedIdempotencyKeys.ContainsKey(idempotencyKey))
            {
                var cachedResponse = req.CreateResponse(HttpStatusCode.OK);
                await cachedResponse.WriteAsJsonAsync(new { message = "Request already processed (idempotent)" });
                return cachedResponse;
            }

            // Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Valid authentication token is required" });
                return unauthorized;
            }

            // Parse request
            var requestBody = await req.ReadAsStringAsync();
            var statusRequest = JsonSerializer.Deserialize<StatusChangeRequest>(requestBody ?? "{}");

            // Validate incident
            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID format" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Validate state transition
            if (!IsValidStatusTransition(incident.Status, newStatus))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = $"Cannot transition from '{incident.Status}' to '{newStatus}'" });
                return conflict;
            }

            // Find responder assignment
            var assignment = incident.ResponderAssignments.FirstOrDefault(a => a.ResponderId == userId.Value);
            if (assignment == null)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You are not assigned to this incident" });
                return forbidden;
            }

            // Update assignment status
            assignment.Status = newStatus;
            if (newStatus == "en_route")
            {
                assignment.EnRouteAt = statusRequest?.Timestamp ?? DateTime.UtcNow;
            }
            else if (newStatus == "on_scene")
            {
                assignment.OnSceneAt = statusRequest?.Timestamp ?? DateTime.UtcNow;
                if (assignment.AcceptedAt.HasValue)
                {
                    assignment.ResponseTimeSeconds = (int)((statusRequest?.Timestamp ?? DateTime.UtcNow) - assignment.AcceptedAt.Value).TotalSeconds;
                }
            }

            // Update incident status
            incident.Status = newStatus;
            incident.UpdatedAt = DateTime.UtcNow;

            await _incidentRepository.UpdateAsync(incident);

            // Store idempotency key
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _processedIdempotencyKeys[idempotencyKey] = incidentId;
            }

            // Send notifications
            var notificationMessage = newStatus == "en_route"
                ? "A responder is en route to your location"
                : "A responder has arrived at your location";

            await _notificationService.SendIncidentNotificationAsync(
                incident.SummonerId,
                incidentGuid,
                eventName,
                notificationMessage);

            // Broadcast real-time update
            await _realtimeService.BroadcastIncidentEventAsync(
                incidentGuid,
                eventName,
                new
                {
                    responderId = userId.Value,
                    role = assignment.Role,
                    location = statusRequest?.CurrentLocation,
                    timestamp = statusRequest?.Timestamp ?? DateTime.UtcNow
                });

            _logger.LogInformation(
                "Incident {IncidentId} status updated to {Status} by responder {ResponderId}",
                incidentId, newStatus, userId.Value);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new IncidentDetailsDto
            {
                IncidentId = incident.IncidentId,
                SummonerId = incident.SummonerId,
                Status = incident.Status,
                IncidentType = incident.IncidentType,
                Description = incident.Description,
                IsMedicalEmergency = incident.IsMedicalEmergency,
                Location = new LocationDto
                {
                    Latitude = incident.LocationLat,
                    Longitude = incident.LocationLng,
                    Geohash = incident.LocationGeohash,
                    Address = incident.LocationAddress
                },
                CreatedAt = incident.ReportedAt,
                ResolvedAt = incident.ResolvedAt,
                ResponderAssignments = incident.ResponderAssignments.Select(a => new ResponderRoleDto
                {
                    AssignmentId = a.AssignmentId,
                    IncidentId = a.IncidentId,
                    ResponderId = a.ResponderId,
                    Role = a.Role,
                    Status = a.Status,
                    AssignedAt = a.AssignedAt,
                    ResponseTimeSeconds = a.ResponseTimeSeconds
                }).ToList()
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating incident {IncidentId} status to {Status}", incidentId, newStatus);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = $"Failed to update incident status to {newStatus}" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Validates incident state machine transitions
    /// </summary>
    private bool IsValidStatusTransition(string currentStatus, string newStatus)
    {
        // Define valid state transitions
        var validTransitions = new Dictionary<string, HashSet<string>>
        {
            ["dispatch_in_progress"] = new HashSet<string> { "awaiting_response", "en_route", "escalation_required" },
            ["awaiting_response"] = new HashSet<string> { "en_route", "escalation_required" },
            ["en_route"] = new HashSet<string> { "on_scene", "escalation_required" },
            ["on_scene"] = new HashSet<string> { "de_escalating", "resolved", "escalation_required" },
            ["de_escalating"] = new HashSet<string> { "resolved", "escalation_required" },
            ["resolved"] = new HashSet<string>(),  // Terminal state
            ["escalation_required"] = new HashSet<string> { "resolved" }  // Can only be resolved after escalation
        };

        return validTransitions.ContainsKey(currentStatus) && validTransitions[currentStatus].Contains(newStatus);
    }
}

// DTOs for incident operations

public class CreateIncidentRequest
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }
    public string? Building { get; set; }
    public string? IncidentType { get; set; }
    public string? Description { get; set; }
    public bool? IsMedicalEmergency { get; set; }
    public bool? SummonerSafe { get; set; }
    public Guid? TriggerPhraseId { get; set; }
}

public class CreateIncidentResponse
{
    public Guid IncidentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Geohash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class IncidentActionSyncRequest
{
    public Guid IncidentId { get; set; }
    public List<OfflineIncidentAction> Actions { get; set; } = new();
}

public class OfflineIncidentAction
{
    public string ActionType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? IdempotencyKey { get; set; }
    public object Payload { get; set; } = new();
}

public class IncidentActionSyncResponse
{
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public List<ActionSyncResult> Results { get; set; } = new();
}

public class ActionSyncResult
{
    public string ActionType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class IncidentDetailsDto
{
    public Guid IncidentId { get; set; }
    public Guid SummonerId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IncidentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsMedicalEmergency { get; set; }
    public LocationDto Location { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public List<ResponderRoleDto> ResponderAssignments { get; set; } = new();
}

public class ResponderRoleDto
{
    public Guid AssignmentId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid ResponderId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
    public int? ResponseTimeSeconds { get; set; }
}

public class AcceptDispatchRequest
{
    public Guid ResponderId { get; set; }
    public int EstimatedArrivalMinutes { get; set; }
}

public class StatusChangeRequest
{
    public Guid ResponderId { get; set; }
    public LocationDto? CurrentLocation { get; set; }
    public DateTime? Timestamp { get; set; }
}

public class ResolveIncidentRequest
{
    public Guid ResponderId { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool AllClear { get; set; }
}

public class LocationDto
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }
    public string? Geohash { get; set; }
}

public class DeclineDispatchRequest
{
    public Guid ResponderId { get; set; }
    public string? Reason { get; set; }
}

public class ResponderDistressRequest
{
    public Guid IncidentId { get; set; }
    public Guid ResponderId { get; set; }
    public LocationDto? Location { get; set; }
    public string? NatureOfDistress { get; set; }
    public DateTime? Timestamp { get; set; }
}

public class ResponderDistressResponse
{
    public Guid AlertId { get; set; }
    public string EscalationStatus { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}
