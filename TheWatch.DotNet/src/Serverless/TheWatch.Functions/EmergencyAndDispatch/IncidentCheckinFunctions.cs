using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Functions.Utilities;
using TheWatch.Infrastructure.Data;

namespace TheWatch.Functions;

/// <summary>
/// Serverless Azure Functions handling Incident Check-in, Summoner Reporting, and Responder Validation (incident-checkin-api.yaml).
/// </summary>
public class IncidentCheckinFunctions
{
    private readonly ILogger<IncidentCheckinFunctions> _logger;
    private readonly IIncidentRepository _incidentRepository;
    private readonly INotificationService _notificationService;
    private readonly WatchDbContext _dbContext;

    public IncidentCheckinFunctions(
        ILogger<IncidentCheckinFunctions> logger,
        IIncidentRepository incidentRepository,
        INotificationService notificationService,
        WatchDbContext dbContext)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _incidentRepository = incidentRepository ?? throw new ArgumentNullException(nameof(incidentRepository));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    [Function("CheckinReportIncident")]
    public async Task<HttpResponseData> ReportIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "checkin/incidents")] HttpRequestData req)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var userId = JwtUtilities.GetUserIdFromClaims(principal) ?? Guid.NewGuid();
        var body = await req.ReadFromJsonAsync<CheckinIncidentReportRequest>();

        var incident = new Incident
        {
            IncidentId = Guid.NewGuid(),
            SummonerId = userId,
            Status = "dispatching",
            IncidentType = body?.IncidentType ?? "emergency",
            Description = body?.Description ?? "Emergency assistance summoned",
            LocationLat = body?.LocationLat,
            LocationLng = body?.LocationLng,
            LocationAddress = body?.Address,
            LocationGeohash = body?.Geohash ?? string.Empty,
            ReportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _incidentRepository.CreateAsync(incident);
        _logger.LogInformation("Reported incident {IncidentId} by user {UserId}", incident.IncidentId, userId);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            incidentId = incident.IncidentId,
            status = incident.Status,
            reportedAt = incident.ReportedAt,
            message = "Incident reported successfully. Dispatch in progress."
        });
        return response;
    }

    [Function("CheckinQueryIncidents")]
    public async Task<HttpResponseData> QueryIncidents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "checkin/incidents")] HttpRequestData req)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var incidents = await _dbContext.Incidents
            .OrderByDescending(i => i.ReportedAt)
            .Take(50)
            .Select(i => new
            {
                incidentId = i.IncidentId,
                status = i.Status,
                incidentType = i.IncidentType,
                description = i.Description,
                reportedAt = i.ReportedAt,
                locationLat = i.LocationLat,
                locationLng = i.LocationLng
            })
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { count = incidents.Count, incidents = incidents });
        return response;
    }

    [Function("CheckinResponderStatus")]
    public async Task<HttpResponseData> CheckinResponder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "checkin/incidents/{incidentId}/checkin")] HttpRequestData req,
        Guid incidentId)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var responderId = JwtUtilities.GetUserIdFromClaims(principal) ?? Guid.NewGuid();
        var body = await req.ReadFromJsonAsync<CheckinStatusUpdateRequest>();

        var incident = await _incidentRepository.GetByIdAsync(incidentId);
        if (incident == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Incident not found" });
            return notFound;
        }

        var assignment = incident.ResponderAssignments.FirstOrDefault(a => a.ResponderId == responderId);
        if (assignment != null)
        {
            assignment.Status = body?.Status ?? "on_scene";
            if (assignment.Status == "on_scene" && assignment.ArrivedAt == null)
            {
                assignment.ArrivedAt = DateTime.UtcNow;
            }
            await _incidentRepository.UpdateAsync(incident);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            incidentId = incidentId,
            responderId = responderId,
            status = body?.Status ?? "on_scene",
            checkedInAt = DateTime.UtcNow
        });
        return response;
    }

    [Function("CheckinFlagDisagreement")]
    public async Task<HttpResponseData> FlagDisagreement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "checkin/incidents/{incidentId}/disagreements")] HttpRequestData req,
        Guid incidentId)
    {
        var principal = JwtUtilities.ValidateJwtFromHeader(req.Headers);
        if (principal == null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauth;
        }

        var responderId = JwtUtilities.GetUserIdFromClaims(principal) ?? Guid.NewGuid();
        var body = await req.ReadFromJsonAsync<CheckinDisagreementRequest>();

        var disagreement = new Disagreement
        {
            DisagreementId = Guid.NewGuid(),
            IncidentId = incidentId,
            ReportingResponderId = responderId,
            DisagreementReason = body?.Reason ?? "Assessment mismatch",
            Notes = body?.Notes,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Disagreements.Add(disagreement);
        await _dbContext.SaveChangesAsync();

        _logger.LogWarning("Disagreement flagged on incident {IncidentId} by responder {ResponderId}", incidentId, responderId);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            disagreementId = disagreement.DisagreementId,
            incidentId = incidentId,
            status = disagreement.Status,
            message = "Disagreement flagged and escalated to HQ review."
        });
        return response;
    }
}

public class CheckinIncidentReportRequest
{
    public string? IncidentType { get; set; }
    public string? Description { get; set; }
    public double? LocationLat { get; set; }
    public double? LocationLng { get; set; }
    public string? Address { get; set; }
    public string? Geohash { get; set; }
}

public class CheckinStatusUpdateRequest
{
    public string? Status { get; set; }
    public string? Notes { get; set; }
}

public class CheckinDisagreementRequest
{
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}
