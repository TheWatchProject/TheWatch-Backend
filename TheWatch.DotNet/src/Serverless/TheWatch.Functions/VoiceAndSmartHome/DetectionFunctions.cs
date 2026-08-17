using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for incident detection and trigger phrase management.
/// Implements endpoints from incident-detection-api.yaml
/// </summary>
public class DetectionFunctions
{
    private readonly ILogger<DetectionFunctions> _logger;
    private readonly ITriggerPhraseRepository _triggerPhraseRepository;
    private readonly IDuressPinRepository _duressPinRepository;
    private readonly IIncidentRepository _incidentRepository;

    public DetectionFunctions(
        ILogger<DetectionFunctions> logger,
        ITriggerPhraseRepository triggerPhraseRepository,
        IDuressPinRepository duressPinRepository,
        IIncidentRepository incidentRepository)
    {
        _logger = logger;
        _triggerPhraseRepository = triggerPhraseRepository;
        _duressPinRepository = duressPinRepository;
        _incidentRepository = incidentRepository;
    }

    /// <summary>
    /// POST /users/{userId}/trigger-phrases - Create a new trigger phrase
    /// </summary>
    [Function("CreateTriggerPhrase")]
    public async Task<HttpResponseData> CreateTriggerPhrase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{userId}/trigger-phrases")] HttpRequestData req,
        string userId)
    {
        _logger.LogInformation("Creating trigger phrase for user: {UserId}", userId);

        try
        {
            if (!Guid.TryParse(userId, out var userGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_USER_ID", message = "Invalid user ID format" });
                return badRequest;
            }

            // TODO: Validate JWT token and ensure userId matches authenticated user

            var requestBody = await req.ReadFromJsonAsync<TriggerPhraseCreateRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            // Validate phrase requirements
            if (string.IsNullOrWhiteSpace(requestBody.Phrase) || requestBody.Phrase.Length < 2 || requestBody.Phrase.Length > 100)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_PHRASE", message = "Phrase must be between 2 and 100 characters" });
                return badRequest;
            }

            // Create trigger phrase entity
            var triggerPhrase = new TriggerPhrase
            {
                PhraseId = Guid.NewGuid(),
                UserId = userGuid,
                PhraseText = requestBody.Phrase,
                AlternativePhrases = requestBody.AlternativePhrases != null ? JsonSerializer.Serialize(requestBody.AlternativePhrases) : null,
                ResponseType = requestBody.ResponseType ?? "community_only",
                Priority = requestBody.Priority ?? "high",
                ConfirmationRequired = requestBody.ConfirmationRequired ?? false,
                ConfirmationTimeoutSeconds = requestBody.ConfirmationTimeoutSeconds ?? 10,
                FirstResponderTypes = requestBody.FirstResponderTypes != null ? JsonSerializer.Serialize(requestBody.FirstResponderTypes) : null,
                FeedbackMode = requestBody.FeedbackMode ?? "standard",
                DeceptiveDisguiseApp = requestBody.DeceptiveAppDisguise,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var created = await _triggerPhraseRepository.CreateAsync(triggerPhrase);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(MapToTriggerPhraseResponse(created));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating trigger phrase");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while creating the trigger phrase" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /users/{userId}/trigger-phrases - Get all trigger phrases for a user
    /// </summary>
    [Function("GetUserTriggerPhrases")]
    public async Task<HttpResponseData> GetUserTriggerPhrases(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/trigger-phrases")] HttpRequestData req,
        string userId)
    {
        _logger.LogInformation("Getting trigger phrases for user: {UserId}", userId);

        try
        {
            if (!Guid.TryParse(userId, out var userGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_USER_ID", message = "Invalid user ID format" });
                return badRequest;
            }

            // TODO: Validate JWT token and ensure userId matches authenticated user

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            bool? isActive = query["isActive"] != null ? bool.Parse(query["isActive"]!) : null;
            string? responseType = query["responseType"];

            var phrases = await _triggerPhraseRepository.GetUserPhrasesAsync(userGuid, isActive, responseType);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                phrases = phrases.Select(MapToTriggerPhraseResponse),
                totalCount = phrases.Count()
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trigger phrases");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while retrieving trigger phrases" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /users/{userId}/trigger-phrases/{phraseId} - Get a specific trigger phrase
    /// </summary>
    [Function("GetTriggerPhrase")]
    public async Task<HttpResponseData> GetTriggerPhrase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/trigger-phrases/{phraseId}")] HttpRequestData req,
        string userId,
        string phraseId)
    {
        _logger.LogInformation("Getting trigger phrase {PhraseId} for user: {UserId}", phraseId, userId);

        try
        {
            if (!Guid.TryParse(userId, out var userGuid) || !Guid.TryParse(phraseId, out var phraseGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_ID", message = "Invalid user ID or phrase ID format" });
                return badRequest;
            }

            // TODO: Validate JWT token and ensure userId matches authenticated user

            var phrase = await _triggerPhraseRepository.GetByIdAsync(phraseGuid, userGuid);
            if (phrase == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { code = "PHRASE_NOT_FOUND", message = "Trigger phrase not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToTriggerPhraseResponse(phrase));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trigger phrase");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while retrieving the trigger phrase" });
            return errorResponse;
        }
    }

    /// <summary>
    /// PUT /users/{userId}/trigger-phrases/{phraseId} - Update a trigger phrase
    /// </summary>
    [Function("UpdateTriggerPhrase")]
    public async Task<HttpResponseData> UpdateTriggerPhrase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{userId}/trigger-phrases/{phraseId}")] HttpRequestData req,
        string userId,
        string phraseId)
    {
        _logger.LogInformation("Updating trigger phrase {PhraseId} for user: {UserId}", phraseId, userId);

        try
        {
            if (!Guid.TryParse(userId, out var userGuid) || !Guid.TryParse(phraseId, out var phraseGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_ID", message = "Invalid user ID or phrase ID format" });
                return badRequest;
            }

            // TODO: Validate JWT token and ensure userId matches authenticated user

            var existing = await _triggerPhraseRepository.GetByIdAsync(phraseGuid, userGuid);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { code = "PHRASE_NOT_FOUND", message = "Trigger phrase not found" });
                return notFound;
            }

            var requestBody = await req.ReadFromJsonAsync<TriggerPhraseUpdateRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            // Update fields if provided
            if (requestBody.Phrase != null)
            {
                if (requestBody.Phrase.Length < 2 || requestBody.Phrase.Length > 100)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { code = "INVALID_PHRASE", message = "Phrase must be between 2 and 100 characters" });
                    return badRequest;
                }
                existing.PhraseText = requestBody.Phrase;
            }

            if (requestBody.AlternativePhrases != null)
                existing.AlternativePhrases = JsonSerializer.Serialize(requestBody.AlternativePhrases);

            if (requestBody.ResponseType != null)
                existing.ResponseType = requestBody.ResponseType;

            if (requestBody.Priority != null)
                existing.Priority = requestBody.Priority;

            if (requestBody.ConfirmationRequired.HasValue)
                existing.ConfirmationRequired = requestBody.ConfirmationRequired.Value;

            if (requestBody.ConfirmationTimeoutSeconds.HasValue)
                existing.ConfirmationTimeoutSeconds = requestBody.ConfirmationTimeoutSeconds.Value;

            if (requestBody.FirstResponderTypes != null)
                existing.FirstResponderTypes = JsonSerializer.Serialize(requestBody.FirstResponderTypes);

            if (requestBody.FeedbackMode != null)
                existing.FeedbackMode = requestBody.FeedbackMode;

            if (requestBody.DeceptiveAppDisguise != null)
                existing.DeceptiveDisguiseApp = requestBody.DeceptiveAppDisguise;

            if (requestBody.IsActive.HasValue)
                existing.IsActive = requestBody.IsActive.Value;

            existing.UpdatedAt = DateTime.UtcNow;

            var updated = await _triggerPhraseRepository.UpdateAsync(existing);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToTriggerPhraseResponse(updated));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating trigger phrase");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while updating the trigger phrase" });
            return errorResponse;
        }
    }

    /// <summary>
    /// DELETE /users/{userId}/trigger-phrases/{phraseId} - Delete a trigger phrase
    /// </summary>
    [Function("DeleteTriggerPhrase")]
    public async Task<HttpResponseData> DeleteTriggerPhrase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/{userId}/trigger-phrases/{phraseId}")] HttpRequestData req,
        string userId,
        string phraseId)
    {
        _logger.LogInformation("Deleting trigger phrase {PhraseId} for user: {UserId}", phraseId, userId);

        try
        {
            if (!Guid.TryParse(userId, out var userGuid) || !Guid.TryParse(phraseId, out var phraseGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_ID", message = "Invalid user ID or phrase ID format" });
                return badRequest;
            }

            // TODO: Validate JWT token and ensure userId matches authenticated user

            var exists = await _triggerPhraseRepository.ExistsAsync(phraseGuid, userGuid);
            if (!exists)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { code = "PHRASE_NOT_FOUND", message = "Trigger phrase not found" });
                return notFound;
            }

            await _triggerPhraseRepository.DeleteAsync(phraseGuid, userGuid);

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting trigger phrase");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while deleting the trigger phrase" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /detection/trigger - Process detected phrase and trigger response
    /// CRITICAL: This endpoint creates an incident when a trigger phrase is detected
    /// </summary>
    [Function("ProcessTriggerDetection")]
    public async Task<HttpResponseData> ProcessTriggerDetection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "detection/trigger")] HttpRequestData req)
    {
        _logger.LogInformation("Processing trigger detection");

        try
        {
            var requestBody = await req.ReadFromJsonAsync<TriggerDetectionRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            // TODO: Validate session and phrase match
            // TODO: Get user ID from session

            // For now, assume we get userId from the phrase lookup
            var phrase = await _triggerPhraseRepository.GetByIdAsync(requestBody.MatchedPhraseId, Guid.Empty); // Need to query differently
            if (phrase == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_PHRASE", message = "Matched phrase not found" });
                return badRequest;
            }

            // Calculate geohash from location
            string geohash = CalculateGeohash(requestBody.Location.Latitude, requestBody.Location.Longitude);

            // Create incident
            var incident = new Incident
            {
                IncidentId = Guid.NewGuid(),
                SummonerId = phrase.UserId,
                Status = phrase.ConfirmationRequired ? "pending_confirmation" : "dispatch_in_progress",
                IncidentType = "other",
                LocationLat = requestBody.Location.Latitude,
                LocationLng = requestBody.Location.Longitude,
                LocationGeohash = geohash,
                LocationAddress = requestBody.Location.Address,
                ReportedAt = DateTime.UtcNow,
                TriggeredPhraseId = phrase.PhraseId,
                DuressFlag = false
            };

            var createdIncident = await _incidentRepository.CreateAsync(incident);

            // Update phrase statistics
            phrase.TriggerCount++;
            phrase.LastTriggeredAt = DateTime.UtcNow;
            await _triggerPhraseRepository.UpdateAsync(phrase);

            // TODO: Queue dispatch notification job
            // TODO: Send feedback to device based on phrase.FeedbackMode

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                incidentId = createdIncident.IncidentId,
                triggeredPhraseId = phrase.PhraseId,
                responseType = phrase.ResponseType,
                status = createdIncident.Status,
                confirmationRequired = phrase.ConfirmationRequired,
                confirmationDeadline = phrase.ConfirmationRequired ? DateTime.UtcNow.AddSeconds(phrase.ConfirmationTimeoutSeconds) : (DateTime?)null,
                triggeredAt = createdIncident.ReportedAt,
                feedbackMode = phrase.FeedbackMode,
                feedbackDelivered = true,
                deceptiveDisguiseActive = phrase.FeedbackMode == "deceptive" ? phrase.DeceptiveDisguiseApp : null
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing trigger detection");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while processing the trigger" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /detection/cancel - Cancel a triggered response
    /// CRITICAL: Handles duress PIN detection - if duress PIN entered, returns 200 OK but silently escalates
    /// </summary>
    [Function("CancelTrigger")]
    public async Task<HttpResponseData> CancelTrigger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "detection/cancel")] HttpRequestData req)
    {
        _logger.LogInformation("Processing trigger cancellation");

        try
        {
            var requestBody = await req.ReadFromJsonAsync<CancelTriggerRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            var incident = await _incidentRepository.GetByIdAsync(requestBody.IncidentId);
            if (incident == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { code = "INCIDENT_NOT_FOUND", message = "Incident not found" });
                return notFound;
            }

            // Verify user owns this incident
            if (incident.SummonerId != requestBody.UserId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Not authorized to cancel this incident" });
                return forbidden;
            }

            bool isDuressPin = false;
            bool isSafePin = false;

            // Check if cancellation code provided
            if (!string.IsNullOrEmpty(requestBody.CancellationCode))
            {
                // Verify against duress PIN first
                isDuressPin = await _duressPinRepository.VerifyDuressPinAsync(requestBody.UserId, requestBody.CancellationCode);

                if (!isDuressPin)
                {
                    // Check safe PIN
                    isSafePin = await _duressPinRepository.VerifySafePinAsync(requestBody.UserId, requestBody.CancellationCode);

                    if (!isSafePin)
                    {
                        var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                        await unauthorized.WriteAsJsonAsync(new { code = "INVALID_PIN", message = "Invalid cancellation code" });
                        return unauthorized;
                    }
                }
            }

            if (isDuressPin)
            {
                // CRITICAL: Duress PIN detected
                // Return fake "cancelled" response but silently escalate to HQ + Police
                _logger.LogWarning("DURESS PIN USED - Incident {IncidentId} being silently escalated", incident.IncidentId);

                incident.DuressFlag = true;
                incident.Status = "escalation_required";
                await _incidentRepository.UpdateAsync(incident);

                // TODO: Send high-priority alert to HQ
                // TODO: Dispatch police if not already dispatched
                // TODO: Log duress event in audit trail

                // Return fake success response
                var duressResponse = req.CreateResponse(HttpStatusCode.OK);
                await duressResponse.WriteAsJsonAsync(new
                {
                    incidentId = incident.IncidentId,
                    cancelled = true,
                    cancelledAt = DateTime.UtcNow,
                    notificationsSent = 0 // Lie about notifications
                });
                return duressResponse;
            }

            // Normal cancellation
            incident.Status = "resolved";
            incident.ResolvedAt = DateTime.UtcNow;
            await _incidentRepository.UpdateAsync(incident);

            // TODO: Send cancellation notifications to any dispatched responders

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                incidentId = incident.IncidentId,
                cancelled = true,
                cancelledAt = incident.ResolvedAt,
                notificationsSent = 0 // TODO: Return actual count
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling trigger");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while cancelling the trigger" });
            return errorResponse;
        }
    }

    // Helper methods
    private object MapToTriggerPhraseResponse(TriggerPhrase phrase)
    {
        return new
        {
            id = phrase.PhraseId,
            userId = phrase.UserId,
            phrase = phrase.PhraseText,
            alternativePhrases = string.IsNullOrEmpty(phrase.AlternativePhrases)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(phrase.AlternativePhrases),
            responseType = phrase.ResponseType,
            priority = phrase.Priority,
            confirmationRequired = phrase.ConfirmationRequired,
            confirmationTimeoutSeconds = phrase.ConfirmationTimeoutSeconds,
            firstResponderTypes = string.IsNullOrEmpty(phrase.FirstResponderTypes)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(phrase.FirstResponderTypes),
            feedbackMode = phrase.FeedbackMode,
            deceptiveAppDisguise = phrase.DeceptiveDisguiseApp,
            isActive = phrase.IsActive,
            createdAt = phrase.CreatedAt,
            updatedAt = phrase.UpdatedAt,
            lastTriggeredAt = phrase.LastTriggeredAt,
            triggerCount = phrase.TriggerCount
        };
    }

    private string CalculateGeohash(double latitude, double longitude, int precision = 8)
    {
        // Simplified geohash calculation - in production use a proper library
        // This is a placeholder implementation
        return $"{latitude:F4},{longitude:F4}".Replace(".", "").Replace(",", "").Replace("-", "").Substring(0, Math.Min(8, precision));
    }

    // Request/Response DTOs
    private class TriggerPhraseCreateRequest
    {
        public string Phrase { get; set; } = string.Empty;
        public string[]? AlternativePhrases { get; set; }
        public string? ResponseType { get; set; }
        public string? Priority { get; set; }
        public bool? ConfirmationRequired { get; set; }
        public int? ConfirmationTimeoutSeconds { get; set; }
        public string[]? FirstResponderTypes { get; set; }
        public string? FeedbackMode { get; set; }
        public string? DeceptiveAppDisguise { get; set; }
    }

    private class TriggerPhraseUpdateRequest
    {
        public string? Phrase { get; set; }
        public string[]? AlternativePhrases { get; set; }
        public string? ResponseType { get; set; }
        public string? Priority { get; set; }
        public bool? ConfirmationRequired { get; set; }
        public int? ConfirmationTimeoutSeconds { get; set; }
        public string[]? FirstResponderTypes { get; set; }
        public string? FeedbackMode { get; set; }
        public string? DeceptiveAppDisguise { get; set; }
        public bool? IsActive { get; set; }
    }

    private class TriggerDetectionRequest
    {
        public Guid SessionId { get; set; }
        public string DetectedPhrase { get; set; } = string.Empty;
        public Guid MatchedPhraseId { get; set; }
        public double MatchConfidence { get; set; }
        public LocationDto Location { get; set; } = new();
        public string? AudioClipUrl { get; set; }
    }

    private class CancelTriggerRequest
    {
        public Guid IncidentId { get; set; }
        public Guid UserId { get; set; }
        public string? CancellationReason { get; set; }
        public string? CancellationCode { get; set; }
    }

    private class LocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Accuracy { get; set; }
        public string? Address { get; set; }
    }
}
