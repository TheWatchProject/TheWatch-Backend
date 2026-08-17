using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for incident lifecycle management.
/// Implements incident state machine and responder coordination from incident-checkin-api.yaml
/// </summary>
public class IncidentLifecycleFunctions
{
    private readonly ILogger<IncidentLifecycleFunctions> _logger;
    private readonly IIncidentRepository _incidentRepository;
    private readonly INotificationService _notificationService;

    // Valid state transitions for incident state machine
    private static readonly Dictionary<string, string[]> ValidStateTransitions = new()
    {
        ["dispatch_in_progress"] = new[] { "awaiting_response", "escalation_required" },
        ["awaiting_response"] = new[] { "en_route", "escalation_required" },
        ["en_route"] = new[] { "on_scene" },
        ["on_scene"] = new[] { "de_escalating", "resolved" },
        ["de_escalating"] = new[] { "resolved", "escalation_required" },
        ["resolved"] = new string[] { }, // Terminal state
        ["escalation_required"] = new[] { "resolved" } // Can be resolved after escalation
    };

    public IncidentLifecycleFunctions(
        ILogger<IncidentLifecycleFunctions> logger,
        IIncidentRepository incidentRepository,
        INotificationService notificationService)
    {
        _logger = logger;
        _incidentRepository = incidentRepository;
        _notificationService = notificationService;
    }

    /// <summary>
    /// PATCH /incidents/{incidentId}/status - Update incident status
    /// Validates state transitions and broadcasts updates
    /// </summary>
    [Function("UpdateIncidentStatus")]
    public async Task<HttpResponseData> UpdateIncidentStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "incidents/{incidentId}/status")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Updating status for incident {IncidentId}", incidentId);

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var statusUpdate = JsonSerializer.Deserialize<IncidentStatusUpdate>(requestBody!);

            if (statusUpdate == null || string.IsNullOrEmpty(statusUpdate.Status))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid status update" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(Guid.Parse(incidentId));
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Validate state transition
            if (!IsValidStateTransition(incident.Status, statusUpdate.Status))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new
                {
                    error = "Invalid state transition",
                    currentStatus = incident.Status,
                    requestedStatus = statusUpdate.Status
                });
                return conflict;
            }

            // Update incident status
            incident.Status = statusUpdate.Status;
            if (statusUpdate.Status == "resolved")
            {
                incident.ResolvedAt = DateTime.UtcNow;
            }

            await _incidentRepository.UpdateAsync(incident);

            // Broadcast status update to subscribed clients
            await _notificationService.BroadcastIncidentUpdateAsync(incident.IncidentId, new
            {
                incidentId = incident.IncidentId,
                status = incident.Status,
                updatedAt = DateTime.UtcNow
            });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToIncidentDetails(incident));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating incident status");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to update incident status" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/accept - Responder accepts dispatch
    /// Automatically assigns First or Second role based on acceptance order
    /// </summary>
    [Function("AcceptDispatch")]
    public async Task<HttpResponseData> AcceptDispatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/accept")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Responder accepting dispatch for incident {IncidentId}", incidentId);

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var acceptRequest = JsonSerializer.Deserialize<AcceptDispatchRequest>(requestBody!);

            if (acceptRequest == null || acceptRequest.ResponderId == Guid.Empty)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid accept request" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(Guid.Parse(incidentId));
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Check if incident already has 2 responders or is resolved
            var existingAssignments = incident.ResponderAssignments
                .Where(a => a.Status != "declined" && a.Status != "cancelled")
                .ToList();

            if (existingAssignments.Count >= 2 || incident.Status == "resolved")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new
                {
                    error = "Incident already has sufficient responders or is resolved"
                });
                return conflict;
            }

            // Assign role based on order: first acceptance = First, second = Second
            var role = existingAssignments.Count == 0 ? "First" : "Second";

            var assignment = new ResponderAssignment
            {
                AssignmentId = Guid.NewGuid(),
                IncidentId = incident.IncidentId,
                ResponderId = acceptRequest.ResponderId,
                Role = role,
                Status = "assigned",
                AssignedAt = DateTime.UtcNow,
                AcceptedAt = DateTime.UtcNow
            };

            incident.ResponderAssignments.Add(assignment);

            // Update incident status based on responder count
            if (existingAssignments.Count == 0)
            {
                incident.Status = "awaiting_response"; // First responder accepted
            }

            await _incidentRepository.UpdateAsync(incident);

            _logger.LogInformation(
                "Responder {ResponderId} accepted incident {IncidentId} as {Role}",
                acceptRequest.ResponderId, incidentId, role);

            // Notify summoner that responder accepted
            await _notificationService.SendPushNotificationAsync(
                incident.SummonerId,
                "Responder En Route",
                $"A responder is on their way to your location.",
                new Dictionary<string, string>
                {
                    ["incidentId"] = incident.IncidentId.ToString(),
                    ["type"] = "responder_accepted"
                });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new ResponderRoleDto
            {
                AssignmentId = assignment.AssignmentId,
                IncidentId = incident.IncidentId,
                ResponderId = assignment.ResponderId,
                Role = role,
                AssignedAt = assignment.AssignedAt,
                Status = assignment.Status
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting dispatch");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to accept dispatch" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/decline - Responder declines dispatch
    /// Triggers dispatch to next candidate in queue
    /// </summary>
    [Function("DeclineDispatch")]
    public async Task<HttpResponseData> DeclineDispatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/decline")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Responder declining dispatch for incident {IncidentId}", incidentId);

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var declineRequest = JsonSerializer.Deserialize<DeclineDispatchRequest>(requestBody!);

            if (declineRequest == null || declineRequest.ResponderId == Guid.Empty)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid decline request" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(Guid.Parse(incidentId));
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Find responder's assignment
            var assignment = incident.ResponderAssignments
                .FirstOrDefault(a => a.ResponderId == declineRequest.ResponderId);

            if (assignment != null && assignment.Status == "assigned")
            {
                assignment.Status = "declined";
                assignment.DeclinedAt = DateTime.UtcNow;
                await _incidentRepository.UpdateAsync(incident);

                _logger.LogInformation(
                    "Responder {ResponderId} declined incident {IncidentId}",
                    declineRequest.ResponderId, incidentId);

                // TODO: Dispatch to next candidate in queue
            }

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error declining dispatch");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to decline dispatch" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/en-route - Responder marks en-route
    /// Initiates live video streaming from summoner to HQ
    /// </summary>
    [Function("MarkEnRoute")]
    public async Task<HttpResponseData> MarkEnRoute(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/en-route")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Marking responder en-route for incident {IncidentId}", incidentId);

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var statusRequest = JsonSerializer.Deserialize<StatusChangeRequest>(requestBody!);

            if (statusRequest == null || statusRequest.ResponderId == Guid.Empty)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid status change request" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(Guid.Parse(incidentId));
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            var assignment = incident.ResponderAssignments
                .FirstOrDefault(a => a.ResponderId == statusRequest.ResponderId);

            if (assignment == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Responder assignment not found" });
                return notFound;
            }

            assignment.Status = "en_route";
            assignment.EnRouteAt = DateTime.UtcNow;
            incident.Status = "en_route";

            await _incidentRepository.UpdateAsync(incident);

            _logger.LogInformation(
                "Responder {ResponderId} marked en-route for incident {IncidentId}",
                statusRequest.ResponderId, incidentId);

            // Notify summoner that responder is on the way
            await _notificationService.SendPushNotificationAsync(
                incident.SummonerId,
                "Responder En Route",
                "Help is on the way. Stay in a safe location.",
                new Dictionary<string, string>
                {
                    ["incidentId"] = incident.IncidentId.ToString(),
                    ["type"] = "responder_en_route"
                });

            // TODO: Initiate live video stream from summoner device to HQ

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToIncidentDetails(incident));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking en-route");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to mark en-route" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/on-scene - Responder marks on-scene
    /// Marks arrival in response timeline for metrics
    /// </summary>
    [Function("MarkOnScene")]
    public async Task<HttpResponseData> MarkOnScene(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/on-scene")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Marking responder on-scene for incident {IncidentId}", incidentId);

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var statusRequest = JsonSerializer.Deserialize<StatusChangeRequest>(requestBody!);

            if (statusRequest == null || statusRequest.ResponderId == Guid.Empty)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid status change request" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(Guid.Parse(incidentId));
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            var assignment = incident.ResponderAssignments
                .FirstOrDefault(a => a.ResponderId == statusRequest.ResponderId);

            if (assignment == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Responder assignment not found" });
                return notFound;
            }

            assignment.Status = "on_scene";
            assignment.OnSceneAt = DateTime.UtcNow;

            // Calculate response time
            if (assignment.AcceptedAt.HasValue)
            {
                assignment.ResponseTimeSeconds = (int)(DateTime.UtcNow - assignment.AcceptedAt.Value).TotalSeconds;
            }

            incident.Status = "on_scene";

            await _incidentRepository.UpdateAsync(incident);

            _logger.LogInformation(
                "Responder {ResponderId} marked on-scene for incident {IncidentId} (response time: {ResponseTime}s)",
                statusRequest.ResponderId, incidentId, assignment.ResponseTimeSeconds);

            // Notify summoner that responder has arrived
            await _notificationService.SendPushNotificationAsync(
                incident.SummonerId,
                "Responder Arrived",
                "Help has arrived at your location.",
                new Dictionary<string, string>
                {
                    ["incidentId"] = incident.IncidentId.ToString(),
                    ["type"] = "responder_on_scene"
                });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToIncidentDetails(incident));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking on-scene");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to mark on-scene" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/resolved - Mark incident resolved
    /// Moves to All Clear status and triggers cleanup
    /// </summary>
    [Function("MarkResolved")]
    public async Task<HttpResponseData> MarkResolved(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/resolved")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Marking incident {IncidentId} as resolved", incidentId);

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var resolveRequest = JsonSerializer.Deserialize<ResolveIncidentRequest>(requestBody!);

            if (resolveRequest == null || resolveRequest.ResponderId == Guid.Empty)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid resolve request" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(Guid.Parse(incidentId));
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
                return notFound;
            }

            // Verify that requester is First responder
            var firstResponder = incident.ResponderAssignments
                .FirstOrDefault(a => a.Role == "First");

            if (firstResponder == null || firstResponder.ResponderId != resolveRequest.ResponderId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only First responder can resolve incident" });
                return forbidden;
            }

            incident.Status = "resolved";
            incident.ResolvedAt = DateTime.UtcNow;

            await _incidentRepository.UpdateAsync(incident);

            _logger.LogInformation("Incident {IncidentId} marked as resolved", incidentId);

            // Notify summoner that incident is resolved
            await _notificationService.SendPushNotificationAsync(
                incident.SummonerId,
                "Incident Resolved",
                "The situation has been resolved. Thank you for using The Watch.",
                new Dictionary<string, string>
                {
                    ["incidentId"] = incident.IncidentId.ToString(),
                    ["type"] = "incident_resolved"
                });

            // TODO: Trigger video termination
            // TODO: Schedule summoner photo deletion
            // TODO: Trigger post-incident review workflow

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToIncidentDetails(incident));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving incident");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to resolve incident" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Validate state machine transitions
    /// </summary>
    private bool IsValidStateTransition(string currentStatus, string newStatus)
    {
        if (currentStatus == newStatus) return true; // Allow idempotent updates

        if (!ValidStateTransitions.TryGetValue(currentStatus, out var validNextStates))
        {
            return false;
        }

        return validNextStates.Contains(newStatus);
    }

    /// <summary>
    /// Map entity to DTO for API responses
    /// </summary>
    private IncidentDetailsDto MapToIncidentDetails(Incident incident)
    {
        return new IncidentDetailsDto
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
        };
    }
}

// DTOs matching incident-checkin-api.yaml

public class IncidentStatusUpdate
{
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
