using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Interfaces;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for incident cancellation with duress PIN detection.
/// CRITICAL: Implements covert escalation for duress situations.
/// </summary>
public class IncidentCancellationFunctions
{
    private readonly ILogger<IncidentCancellationFunctions> _logger;
    private readonly IDuressPinService _duressPinService;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IAdminAuditRepository _auditRepository;

    public IncidentCancellationFunctions(
        ILogger<IncidentCancellationFunctions> logger,
        IDuressPinService duressPinService,
        IIncidentRepository incidentRepository,
        IAdminAuditRepository auditRepository)
    {
        _logger = logger;
        _duressPinService = duressPinService;
        _incidentRepository = incidentRepository;
        _auditRepository = auditRepository;
    }

    /// <summary>
    /// POST /incidents/{incidentId}/cancel - Cancel incident with PIN verification
    /// CRITICAL: Implements duress PIN detection with covert escalation
    /// </summary>
    [Function("CancelIncident")]
    public async Task<HttpResponseData> CancelIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/cancel")] HttpRequestData req,
        string incidentId)
    {
        try
        {
            // TODO: Extract userId from JWT token
            var userId = Guid.Empty; // Placeholder

            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_INCIDENT_ID", "Invalid incident ID format");
            }

            var body = await JsonSerializer.DeserializeAsync<CancelIncidentRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.Pin))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "PIN is required for cancellation");
            }

            // Verify incident exists and belongs to user
            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "INCIDENT_NOT_FOUND", "Incident not found");
            }

            if (incident.SummonerId != userId)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "NOT_AUTHORIZED", "Not authorized to cancel this incident");
            }

            // Verify incident can be cancelled
            if (incident.Status == "resolved" || incident.Status == "cancelled")
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "ALREADY_RESOLVED", "Incident already resolved");
            }

            // CRITICAL: Validate cancellation PIN (duress detection)
            var (isValidPin, isDuressPin) = await _duressPinService.ValidateCancellationPinAsync(
                userId,
                body.Pin,
                incidentGuid);

            if (!isValidPin)
            {
                // Invalid PIN - reject cancellation
                _logger.LogWarning("Invalid cancellation PIN for incident {IncidentId}", incidentId);
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_PIN", "Invalid cancellation PIN");
            }

            // CRITICAL: Both duress and safe PIN return success
            // The difference is handled internally by DuressPinService
            if (isDuressPin)
            {
                // Duress PIN detected - silent escalation already triggered
                // Return 200 OK to maintain covertness
                _logger.LogInformation("Incident cancellation processed for {IncidentId}", incidentId);

                // Update incident status to "cancelled" (client sees cancellation)
                // But actual status is "duress_escalated" (internal)
                incident.Status = "cancelled"; // This will be overridden by DuressPinService
                incident.ResolvedAt = DateTime.UtcNow;
                incident.UpdatedAt = DateTime.UtcNow;

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    incident_id = incidentGuid,
                    status = "cancelled",
                    message = "Incident cancelled successfully"
                });
                return response;
            }
            else
            {
                // Safe PIN - legitimate cancellation
                incident.Status = "cancelled";
                incident.ResolvedAt = DateTime.UtcNow;
                incident.UpdatedAt = DateTime.UtcNow;
                await _incidentRepository.UpdateAsync(incident);

                // Audit log
                _logger.LogInformation("Incident {IncidentId} cancelled by user {UserId}", incidentId, userId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    incident_id = incidentGuid,
                    status = "cancelled",
                    message = "Incident cancelled successfully"
                });
                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling incident {IncidentId}", incidentId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during cancellation");
        }
    }

    /// <summary>
    /// POST /incidents/{incidentId}/cancel/voice - Voice-initiated cancellation (no PIN)
    /// Used when voice command is detected for cancellation
    /// </summary>
    [Function("CancelIncidentVoice")]
    public async Task<HttpResponseData> CancelIncidentVoice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "incidents/{incidentId}/cancel/voice")] HttpRequestData req,
        string incidentId)
    {
        try
        {
            // TODO: Extract userId from JWT token
            var userId = Guid.Empty; // Placeholder

            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_INCIDENT_ID", "Invalid incident ID format");
            }

            var body = await JsonSerializer.DeserializeAsync<VoiceCancelRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Request body required");
            }

            var incident = await _incidentRepository.GetByIdAsync(incidentGuid);
            if (incident == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "INCIDENT_NOT_FOUND", "Incident not found");
            }

            // Voice cancellation within first 30 seconds doesn't require PIN
            var incidentAge = DateTime.UtcNow - incident.CreatedAt;
            if (incidentAge.TotalSeconds > 30)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "PIN_REQUIRED", "PIN required for cancellation after 30 seconds");
            }

            incident.Status = "cancelled";
            incident.ResolvedAt = DateTime.UtcNow;
            incident.UpdatedAt = DateTime.UtcNow;
            await _incidentRepository.UpdateAsync(incident);

            _logger.LogInformation("Incident {IncidentId} cancelled via voice by user {UserId}", incidentId, userId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                incident_id = incidentGuid,
                status = "cancelled",
                message = "Incident cancelled successfully"
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling incident via voice {IncidentId}", incidentId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during cancellation");
        }
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string code, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { code, message });
        return response;
    }

    // Request DTOs
    private class CancelIncidentRequest
    {
        public string Pin { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    private class VoiceCancelRequest
    {
        public string? VoiceCommand { get; set; }
        public double? Confidence { get; set; }
    }
}
