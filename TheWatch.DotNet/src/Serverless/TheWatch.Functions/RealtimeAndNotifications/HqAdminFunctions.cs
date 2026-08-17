using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for HQ administrative operations.
/// Implements endpoints from hq-admin-api.yaml
/// All endpoints require 'hq' or 'admin' role in JWT
/// </summary>
public class HqAdminFunctions
{
    private readonly ILogger<HqAdminFunctions> _logger;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IEvidenceRepository _evidenceRepository;
    private readonly IAdminAuditRepository _adminAuditRepository;
    private readonly IHqBroadcastService _hqBroadcastService;
    private readonly INotificationService _notificationService;

    public HqAdminFunctions(
        ILogger<HqAdminFunctions> logger,
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IEvidenceRepository evidenceRepository,
        IAdminAuditRepository adminAuditRepository,
        IHqBroadcastService hqBroadcastService,
        INotificationService notificationService)
    {
        _logger = logger;
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _evidenceRepository = evidenceRepository;
        _adminAuditRepository = adminAuditRepository;
        _hqBroadcastService = hqBroadcastService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// GET /hq/incidents/active - Get all active incidents dashboard view
    /// Real-time dashboard for HQ operators to monitor ongoing incidents
    /// </summary>
    [Function("GetActiveIncidentsDashboard")]
    public async Task<HttpResponseData> GetActiveIncidentsDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hq/incidents/active")] HttpRequestData req)
    {
        _logger.LogInformation("Getting active incidents dashboard");

        // 1. Validate HQ/admin authentication
        if (!JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        // 2. Parse query parameters
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var severity = query["severity"];
        var hasDisagreementStr = query["hasDisagreement"];
        var hasDistressStr = query["hasDistress"];
        var limitStr = query["limit"] ?? "50";
        var cursor = query["cursor"];

        bool? hasDisagreement = hasDisagreementStr != null ? bool.Parse(hasDisagreementStr) : null;
        bool? hasDistress = hasDistressStr != null ? bool.Parse(hasDistressStr) : null;
        int limit = int.Parse(limitStr);

        // 3-5. Query incidents with filters
        var activeIncidents = await _incidentRepository.GetActiveIncidentsAsync(
            severity, hasDisagreement, hasDistress, limit, cursor);

        // 6-7. Map to response format with elapsed time calculation
        var now = DateTime.UtcNow;
        var items = activeIncidents.Select(i => new
        {
            incidentId = i.IncidentId,
            status = i.Status,
            location = new
            {
                latitude = i.LocationLat,
                longitude = i.LocationLng,
                address = i.LocationAddress
            },
            assignedResponders = i.ResponderAssignments
                .Where(a => a.Status != "declined")
                .Select(a => new
                {
                    responderId = a.ResponderId,
                    role = a.Role,
                    status = a.Status
                }),
            flags = new
            {
                hasDisagreement = i.Disagreements.Any(d => d.ResolutionStatus == "unreviewed"),
                hasDistress = i.ResponderAssignments.Any(a => a.DistressSignalAt != null),
                isMedical = i.IsMedicalEmergency
            },
            createdAt = i.ReportedAt,
            elapsedTimeSeconds = (int)(now - i.ReportedAt).TotalSeconds
        }).ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            items,
            nextCursor = items.Count >= limit ? items.Last().incidentId.ToString() : (string?)null
        });

        return response;
    }

    /// <summary>
    /// GET /hq/incidents/{incidentId} - Get detailed incident view
    /// Comprehensive incident details for HQ review and intervention
    /// </summary>
    [Function("GetIncidentDetails")]
    public async Task<HttpResponseData> GetIncidentDetails(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hq/incidents/{incidentId}")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Getting incident details: {IncidentId}", incidentId);

        // 1. Validate HQ/admin authentication
        if (!JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        // 2. Parse incidentId as Guid
        if (!Guid.TryParse(incidentId, out var incidentGuid))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID" });
            return badRequest;
        }

        // 3-7. Retrieve full incident details
        var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
        if (incident == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            incidentId = incident.IncidentId,
            summonerId = incident.SummonerId,
            location = new
            {
                latitude = incident.LocationLat,
                longitude = incident.LocationLng,
                address = incident.LocationAddress
            },
            status = incident.Status,
            timeline = incident.TimelineEvents
                .OrderBy(e => e.EventTime)
                .Select(e => new
                {
                    eventType = e.EventType,
                    timestamp = e.EventTime,
                    actorId = e.ActorId?.ToString(),
                    actor = e.ActorType,
                    payload = string.IsNullOrEmpty(e.EventData) ? new { } : JsonSerializer.Deserialize<object>(e.EventData)
                }),
            responders = incident.ResponderAssignments
                .Where(a => a.Status != "declined")
                .Select(a => new
                {
                    responderId = a.ResponderId,
                    role = a.Role,
                    status = a.Status,
                    responseTimeSeconds = a.ResponseTimeSeconds
                }),
            disagreements = incident.Disagreements.Select(d => new
            {
                disagreementId = d.DisagreementId,
                type = d.DisagreementType,
                severity = d.Severity,
                flaggedAt = d.FlaggedAt
            }),
            evidence = incident.Evidence.Select(e => e.EvidenceId)
        });

        return response;
    }

    /// <summary>
    /// POST /hq/incidents/{incidentId}/intervene - HQ intervention on an incident
    /// Allows HQ to broadcast messages, escalate to law enforcement, or take control
    /// </summary>
    [Function("HqIntervene")]
    public async Task<HttpResponseData> HqIntervene(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/incidents/{incidentId}/intervene")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("HQ intervention on incident: {IncidentId}", incidentId);

        // 1. Validate HQ/admin authentication
        if (!JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Invalid authentication" });
            return unauthorized;
        }

        // 2. Parse request body
        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var intervention = JsonSerializer.Deserialize<HqInterventionRequest>(requestBody);
        if (intervention == null)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Invalid intervention request" });
            return badRequest;
        }

        // 4. Parse incidentId
        if (!Guid.TryParse(incidentId, out var incidentGuid))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID" });
            return badRequest;
        }

        // 5. Retrieve incident
        var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
        if (incident == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
            return notFound;
        }

        var interventionId = Guid.NewGuid();
        var affectedResponders = incident.ResponderAssignments
            .Where(a => a.Status != "declined")
            .Select(a => a.ResponderId)
            .ToList();

        // 6. Execute intervention based on action
        switch (intervention.Action.ToLowerInvariant())
        {
            case "send_broadcast":
                await _hqBroadcastService.SendBroadcastAsync(
                    incidentGuid, userId.Value, intervention.Message ?? "HQ Override",
                    intervention.Command, intervention.Severity ?? "critical");
                break;

            case "escalate_law_enforcement":
                // Update incident status and notify authorities
                incident.Status = "escalated";
                incident.PoliceNotified = true;
                await _incidentRepository.UpdateAsync(incident);
                break;

            case "request_backup":
                // Trigger additional responder dispatch
                await _notificationService.BroadcastIncidentUpdateAsync(incidentGuid, new
                {
                    type = "backup_requested",
                    message = "Additional responders needed"
                });
                break;

            case "take_control":
                incident.HqControlTaken = true;
                await _incidentRepository.UpdateAsync(incident);
                break;
        }

        // 7. Create admin audit record
        await _adminAuditRepository.LogActionAsync(
            userId.Value,
            "hq_intervention",
            "incident",
            incidentGuid.ToString(),
            intervention);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            interventionId,
            action = intervention.Action,
            executedAt = DateTime.UtcNow,
            affectedResponders
        });

        return response;
    }

    /// <summary>
    /// GET /hq/disagreements/pending - Get pending disagreement flags for review
    /// Queue of responder disagreements requiring HQ resolution
    /// </summary>
    [Function("GetPendingDisagreements")]
    public async Task<HttpResponseData> GetPendingDisagreements(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hq/disagreements/pending")] HttpRequestData req)
    {
        _logger.LogInformation("Getting pending disagreements");

        // TODO: Implement pending disagreements retrieval logic
        // 1. Validate HQ/admin authentication
        // 2. Parse query parameters: severity, limit
        // 3. Query disagreements table where:
        //    - resolution_status = 'unreviewed'
        //    - Filter by severity if specified (high, medium, low)
        // 4. Join with incidents for context
        // 5. Join with post_incident_reviews for First and Second assessments
        // 6. Order by flagged_at ASC (oldest first), severity DESC
        // 7. Return array of DisagreementReviewItem objects

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new[]
        {
            new
            {
                disagreementId = Guid.NewGuid(),
                incidentId = Guid.NewGuid(),
                firstResponderAssessment = "Situation resolved, no police needed",
                secondResponderFlag = "First responder appeared compromised, summoner showed signs of coercion",
                disagreementType = "safety_concern",
                severity = "high",
                flaggedAt = DateTime.UtcNow.AddHours(-2)
            },
            new
            {
                disagreementId = Guid.NewGuid(),
                incidentId = Guid.NewGuid(),
                firstResponderAssessment = "False alarm, summoner cancelled",
                secondResponderFlag = "Cancellation seemed forced, recommend follow-up",
                disagreementType = "status_conflict",
                severity = "medium",
                flaggedAt = DateTime.UtcNow.AddHours(-5)
            }
        });

        return response;
    }

    /// <summary>
    /// POST /hq/disagreements/{disagreementId}/resolve - Resolve a disagreement flag
    /// HQ reviews and makes final determination on responder disagreement
    /// </summary>
    [Function("ResolveDisagreement")]
    public async Task<HttpResponseData> ResolveDisagreement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/disagreements/{disagreementId}/resolve")] HttpRequestData req,
        string disagreementId)
    {
        _logger.LogInformation("Resolving disagreement: {DisagreementId}", disagreementId);

        // TODO: Implement disagreement resolution logic
        // 1. Validate HQ/admin authentication
        // 2. Parse DisagreementResolution from request body:
        //    - resolution (first_correct, second_correct, both_correct, insufficient_info, training_needed)
        //    - notes (explanation of decision)
        //    - actionsTaken (array of actions: police_notified, responder_training_recommended, etc.)
        // 3. Parse disagreementId as Guid
        // 4. Retrieve disagreements record
        // 5. Update disagreements table:
        //    - resolution_status = 'resolved'
        //    - resolution_type = resolution
        //    - resolution_notes = notes
        //    - resolved_at = NOW()
        //    - resolved_by_admin_id = current_user_id
        // 6. If actionsTaken includes training_needed:
        //    - Flag responder(s) for additional training
        //    - Send training recommendation to responder
        // 7. Create admin_actions_audit record
        // 8. Return DisagreementResolutionResponse

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            disagreementId,
            resolution = "second_correct",
            resolvedAt = DateTime.UtcNow,
            resolvedBy = Guid.NewGuid()
        });

        return response;
    }

    /// <summary>
    /// GET /hq/evidence/{evidenceId} - Get evidence metadata
    /// Retrieve evidence details for HQ review
    /// </summary>
    [Function("GetEvidenceMetadata")]
    public async Task<HttpResponseData> GetEvidenceMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hq/evidence/{evidenceId}")] HttpRequestData req,
        string evidenceId)
    {
        _logger.LogInformation("Getting evidence metadata: {EvidenceId}", evidenceId);

        // TODO: Implement evidence metadata retrieval logic
        // 1. Validate HQ/admin authentication
        // 2. Parse evidenceId as Guid
        // 3. Retrieve evidence record from database
        // 4. Join with evidence_chain_of_custody for custody history
        // 5. Return EvidenceMetadata with storage location, legal hold status, chain of custody

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            evidenceId,
            incidentId = Guid.NewGuid(),
            type = "photo",
            uploadedBy = Guid.NewGuid(),
            uploadedAt = DateTime.UtcNow.AddHours(-3),
            legalHold = (object?)null,
            storageLocation = "blob://evidence/incident-123/photo-456.jpg",
            sizeBytes = 2457600,
            sha256Hash = "abc123def456..."
        });

        return response;
    }

    /// <summary>
    /// POST /hq/evidence/{evidenceId}/legal-hold - Place legal hold on evidence
    /// Prevents deletion of evidence for legal proceedings
    /// </summary>
    [Function("PlaceLegalHold")]
    public async Task<HttpResponseData> PlaceLegalHold(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/evidence/{evidenceId}/legal-hold")] HttpRequestData req,
        string evidenceId)
    {
        _logger.LogInformation("Placing legal hold on evidence: {EvidenceId}", evidenceId);

        // TODO: Implement legal hold placement logic
        // 1. Validate HQ/admin authentication
        // 2. Parse LegalHoldRequest from request body:
        //    - reason (explanation for hold)
        //    - caseNumber (optional)
        //    - requestedBy (name/ID of person requesting)
        //    - expiresAt (optional expiration date)
        // 3. Parse evidenceId as Guid
        // 4. Retrieve evidence record
        // 5. Update evidence table:
        //    - legal_hold = true
        //    - legal_hold_placed_at = NOW()
        // 6. Create evidence_chain_of_custody record (event_type: 'legal_hold_placed')
        // 7. Create admin_actions_audit record
        // 8. Return LegalHoldResponse

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            holdId = Guid.NewGuid(),
            evidenceId,
            placedAt = DateTime.UtcNow,
            placedBy = "HQ Admin - Jane Smith",
            expiresAt = (DateTime?)null
        });

        return response;
    }

    /// <summary>
    /// DELETE /hq/evidence/{evidenceId}/legal-hold - Remove legal hold
    /// Removes legal hold, allowing evidence to follow normal retention policies
    /// Requires 'admin' role (not just 'hq')
    /// </summary>
    [Function("RemoveLegalHold")]
    public async Task<HttpResponseData> RemoveLegalHold(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "hq/evidence/{evidenceId}/legal-hold")] HttpRequestData req,
        string evidenceId)
    {
        _logger.LogInformation("Removing legal hold from evidence: {EvidenceId}", evidenceId);

        // TODO: Implement legal hold removal logic
        // 1. Validate admin authentication (requires 'admin' role, not just 'hq')
        // 2. Parse removal reason from request body
        // 3. Parse evidenceId as Guid
        // 4. Retrieve evidence record
        // 5. Verify legal hold exists
        // 6. Update evidence table:
        //    - legal_hold = false
        //    - legal_hold_removed_at = NOW()
        // 7. Create evidence_chain_of_custody record (event_type: 'legal_hold_removed')
        // 8. Create admin_actions_audit record with removal reason
        // 9. Return 204 No Content

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        return response;
    }

    /// <summary>
    /// POST /hq/users/{userId}/moderate - Moderate user account
    /// Administrative actions on user accounts (suspend, ban, revoke responder status)
    /// Requires 'admin' role
    /// </summary>
    [Function("ModerateUser")]
    public async Task<HttpResponseData> ModerateUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/users/{userId}/moderate")] HttpRequestData req,
        string userId)
    {
        _logger.LogInformation("Moderating user: {UserId}", userId);

        // TODO: Implement user moderation logic
        // 1. Validate admin authentication (requires 'admin' role)
        // 2. Parse ModerationRequest from request body:
        //    - action (suspend, unsuspend, revoke_responder, flag_review, ban)
        //    - reason (explanation)
        //    - duration (optional ISO 8601 duration, e.g., P30D for 30 days)
        //    - notes (additional context)
        // 3. Parse userId as Guid
        // 4. Retrieve users record
        // 5. Execute moderation action:
        //    a. SUSPEND: Set account_status = 'suspended', suspended_until = NOW() + duration
        //    b. UNSUSPEND: Set account_status = 'active', suspended_until = NULL
        //    c. REVOKE_RESPONDER: Update responder_profile.is_responder_eligible = false
        //    d. FLAG_REVIEW: Mark account for manual review
        //    e. BAN: Set account_status = 'banned', revoke all access
        // 6. Create admin_actions_audit record
        // 7. Send notification to user about moderation action
        // 8. Return ModerationResponse

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            userId,
            action = "suspend",
            appliedAt = DateTime.UtcNow,
            appliedBy = Guid.NewGuid()
        });

        return response;
    }

    /// <summary>
    /// GET /hq/analytics/metrics - Get system-wide metrics
    /// Analytics dashboard for HQ monitoring system health and performance
    /// </summary>
    [Function("GetSystemMetrics")]
    public async Task<HttpResponseData> GetSystemMetrics(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hq/analytics/metrics")] HttpRequestData req)
    {
        _logger.LogInformation("Getting system metrics");

        // TODO: Implement system metrics retrieval logic
        // 1. Validate HQ/admin authentication
        // 2. Parse query parameter: timeRange (24h, 7d, 30d, 90d)
        // 3. Query aggregated metrics for timeRange:
        //    a. Incident stats (total, avg response time, escalation rate, false positive rate)
        //    b. Responder stats (active, avg acceptance rate, distress signals)
        //    c. Coverage stats (geographic coverage %, temporal coverage %)
        // 4. Return SystemMetrics

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            timeRange = "24h",
            incidentStats = new
            {
                totalIncidents = 247,
                averageResponseTimeSeconds = 245.3,
                escalationRate = 0.08,
                falsePositiveRate = 0.03
            },
            responderStats = new
            {
                activeResponders = 1523,
                averageAcceptanceRate = 0.87,
                distressSignalsTriggered = 2
            },
            coverageStats = new
            {
                geographicCoveragePercent = 78.5,
                temporalCoveragePercent = 92.3
            }
        });

        return response;
    }
}

public class HqInterventionRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Command { get; set; }
    public string? Severity { get; set; }
}
