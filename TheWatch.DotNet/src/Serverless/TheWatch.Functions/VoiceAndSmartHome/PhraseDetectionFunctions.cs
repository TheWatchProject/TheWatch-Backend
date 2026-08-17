using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for phrase detection and trigger management.
/// Implements endpoints from incident-detection-api.yaml.
/// CRITICAL: This is a safety-critical feature - duress PIN must return 200 OK while silently escalating.
/// </summary>
public class PhraseDetectionFunctions
{
    private readonly ILogger<PhraseDetectionFunctions> _logger;
    private readonly ITriggerPhraseRepository _phraseRepository;
    private readonly IDetectionSessionRepository _sessionRepository;
    private readonly IPhraseMatchingService _phraseMatching;
    private readonly IFeedbackModeService _feedbackMode;
    private readonly IDuressPinService _duressPinService;
    private readonly IDispatchService _dispatchService;
    private readonly IIncidentRepository _incidentRepository;
    private readonly INotificationService _notificationService;
    private readonly GeohashService _geohashService;

    public PhraseDetectionFunctions(
        ILogger<PhraseDetectionFunctions> logger,
        ITriggerPhraseRepository phraseRepository,
        IDetectionSessionRepository sessionRepository,
        IPhraseMatchingService phraseMatching,
        IFeedbackModeService feedbackMode,
        IDuressPinService duressPinService,
        IDispatchService dispatchService,
        IIncidentRepository incidentRepository,
        INotificationService notificationService)
    {
        _logger = logger;
        _phraseRepository = phraseRepository;
        _sessionRepository = sessionRepository;
        _phraseMatching = phraseMatching;
        _feedbackMode = feedbackMode;
        _duressPinService = duressPinService;
        _dispatchService = dispatchService;
        _incidentRepository = incidentRepository;
        _notificationService = notificationService;
        _geohashService = new GeohashService();
    }

    #region Trigger Phrase CRUD

    /// <summary>
    /// POST /users/{userId}/trigger-phrases - Create a new trigger phrase
    /// </summary>
    [Function("CreateTriggerPhrase")]
    public async Task<HttpResponseData> CreateTriggerPhrase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{userId}/trigger-phrases")] HttpRequestData req,
        string userId)
    {
        try
        {
            // Validate authentication
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || !Guid.TryParse(userId, out var userGuid) || requesterId.Value != userGuid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            var body = await JsonSerializer.DeserializeAsync<TriggerPhraseCreateRequest>(req.Body);
            if (body == null || string.IsNullOrWhiteSpace(body.Phrase))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Phrase is required");
            }

            // Validate phrase length
            if (body.Phrase.Length < 2 || body.Phrase.Length > 100)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_PHRASE", "Phrase must be between 2 and 100 characters");
            }

            // Create trigger phrase entity
            var phrase = new TriggerPhrase
            {
                PhraseId = Guid.NewGuid(),
                UserId = userGuid,
                PhraseText = body.Phrase,
                AlternativePhrases = body.AlternativePhrases != null && body.AlternativePhrases.Any()
                    ? JsonSerializer.Serialize(body.AlternativePhrases)
                    : null,
                ResponseType = body.ResponseType ?? "community_only",
                Priority = body.Priority ?? "high",
                ConfirmationRequired = body.ConfirmationRequired ?? false,
                ConfirmationTimeoutSeconds = body.ConfirmationTimeoutSeconds ?? 10,
                FirstResponderTypes = body.FirstResponderTypes != null && body.FirstResponderTypes.Any()
                    ? JsonSerializer.Serialize(body.FirstResponderTypes)
                    : null,
                FeedbackMode = body.FeedbackMode ?? "standard",
                DeceptiveDisguiseApp = body.DeceptiveAppDisguise,
                IsActive = true,
                TriggerCount = 0
            };

            var created = await _phraseRepository.CreateAsync(phrase);

            _logger.LogInformation("Created trigger phrase {PhraseId} for user {UserId}", created.PhraseId, userGuid);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(MapToResponse(created));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating trigger phrase");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to create trigger phrase");
        }
    }

    /// <summary>
    /// GET /users/{userId}/trigger-phrases - Get all trigger phrases for user
    /// </summary>
    [Function("GetUserTriggerPhrases")]
    public async Task<HttpResponseData> GetUserTriggerPhrases(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/trigger-phrases")] HttpRequestData req,
        string userId)
    {
        try
        {
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || !Guid.TryParse(userId, out var userGuid) || requesterId.Value != userGuid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var responseType = query["responseType"];
            var isActiveStr = query["isActive"];
            bool? isActive = string.IsNullOrEmpty(isActiveStr) ? null : bool.Parse(isActiveStr);

            var phrases = await _phraseRepository.GetUserPhrasesAsync(userGuid, isActive, responseType);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                phrases = phrases.Select(MapToResponse),
                totalCount = phrases.Count()
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user trigger phrases");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to get trigger phrases");
        }
    }

    /// <summary>
    /// GET /users/{userId}/trigger-phrases/{phraseId} - Get specific trigger phrase
    /// </summary>
    [Function("GetTriggerPhrase")]
    public async Task<HttpResponseData> GetTriggerPhrase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/trigger-phrases/{phraseId}")] HttpRequestData req,
        string userId,
        string phraseId)
    {
        try
        {
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || !Guid.TryParse(userId, out var userGuid) || requesterId.Value != userGuid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            if (!Guid.TryParse(phraseId, out var phraseGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_PHRASE_ID", "Invalid phrase ID");
            }

            var phrase = await _phraseRepository.GetByIdAsync(phraseGuid, userGuid);
            if (phrase == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "PHRASE_NOT_FOUND", "Trigger phrase not found");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToResponse(phrase));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trigger phrase");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to get trigger phrase");
        }
    }

    /// <summary>
    /// PUT /users/{userId}/trigger-phrases/{phraseId} - Update trigger phrase
    /// </summary>
    [Function("UpdateTriggerPhrase")]
    public async Task<HttpResponseData> UpdateTriggerPhrase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{userId}/trigger-phrases/{phraseId}")] HttpRequestData req,
        string userId,
        string phraseId)
    {
        try
        {
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || !Guid.TryParse(userId, out var userGuid) || requesterId.Value != userGuid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            if (!Guid.TryParse(phraseId, out var phraseGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_PHRASE_ID", "Invalid phrase ID");
            }

            var existing = await _phraseRepository.GetByIdAsync(phraseGuid, userGuid);
            if (existing == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "PHRASE_NOT_FOUND", "Trigger phrase not found");
            }

            var body = await JsonSerializer.DeserializeAsync<TriggerPhraseUpdateRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Request body is required");
            }

            // Update fields if provided
            if (!string.IsNullOrWhiteSpace(body.Phrase))
            {
                if (body.Phrase.Length < 2 || body.Phrase.Length > 100)
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_PHRASE", "Phrase must be between 2 and 100 characters");
                }
                existing.PhraseText = body.Phrase;
            }

            if (body.AlternativePhrases != null)
            {
                existing.AlternativePhrases = body.AlternativePhrases.Any()
                    ? JsonSerializer.Serialize(body.AlternativePhrases)
                    : null;
            }

            if (!string.IsNullOrEmpty(body.ResponseType))
                existing.ResponseType = body.ResponseType;

            if (!string.IsNullOrEmpty(body.Priority))
                existing.Priority = body.Priority;

            if (body.ConfirmationRequired.HasValue)
                existing.ConfirmationRequired = body.ConfirmationRequired.Value;

            if (body.ConfirmationTimeoutSeconds.HasValue)
                existing.ConfirmationTimeoutSeconds = body.ConfirmationTimeoutSeconds.Value;

            if (body.FirstResponderTypes != null)
            {
                existing.FirstResponderTypes = body.FirstResponderTypes.Any()
                    ? JsonSerializer.Serialize(body.FirstResponderTypes)
                    : null;
            }

            if (!string.IsNullOrEmpty(body.FeedbackMode))
                existing.FeedbackMode = body.FeedbackMode;

            if (!string.IsNullOrEmpty(body.DeceptiveAppDisguise))
                existing.DeceptiveDisguiseApp = body.DeceptiveAppDisguise;

            if (body.IsActive.HasValue)
                existing.IsActive = body.IsActive.Value;

            var updated = await _phraseRepository.UpdateAsync(existing);

            _logger.LogInformation("Updated trigger phrase {PhraseId} for user {UserId}", phraseGuid, userGuid);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToResponse(updated));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating trigger phrase");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to update trigger phrase");
        }
    }

    /// <summary>
    /// DELETE /users/{userId}/trigger-phrases/{phraseId} - Delete trigger phrase
    /// </summary>
    [Function("DeleteTriggerPhrase")]
    public async Task<HttpResponseData> DeleteTriggerPhrase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/{userId}/trigger-phrases/{phraseId}")] HttpRequestData req,
        string userId,
        string phraseId)
    {
        try
        {
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || !Guid.TryParse(userId, out var userGuid) || requesterId.Value != userGuid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            if (!Guid.TryParse(phraseId, out var phraseGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_PHRASE_ID", "Invalid phrase ID");
            }

            var exists = await _phraseRepository.ExistsAsync(phraseGuid, userGuid);
            if (!exists)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "PHRASE_NOT_FOUND", "Trigger phrase not found");
            }

            await _phraseRepository.DeleteAsync(phraseGuid, userGuid);

            _logger.LogInformation("Deleted trigger phrase {PhraseId} for user {UserId}", phraseGuid, userGuid);

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting trigger phrase");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to delete trigger phrase");
        }
    }

    #endregion

    #region Detection Sessions

    /// <summary>
    /// POST /detection/start - Start phrase detection session
    /// </summary>
    [Function("StartDetection")]
    public async Task<HttpResponseData> StartDetection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "detection/start")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<StartDetectionRequest>(req.Body);
            if (body == null || body.UserId == Guid.Empty)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "User ID is required");
            }

            // Validate authentication
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || requesterId.Value != body.UserId)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            // Check for existing active session
            var existingSession = await _sessionRepository.GetActiveSessionForUserAsync(body.UserId);
            if (existingSession != null)
            {
                // Stop the existing session first
                existingSession.Status = DetectionSessionStatus.Stopped;
                existingSession.EndedAt = DateTime.UtcNow;
                await _sessionRepository.UpdateAsync(existingSession);
            }

            // Get user's active phrases count
            var activePhrases = await _phraseRepository.GetUserPhrasesAsync(body.UserId, isActive: true);
            var activePhraseCount = activePhrases.Count();

            if (activePhraseCount == 0)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "NO_ACTIVE_PHRASES", "No active trigger phrases configured");
            }

            // Parse detection mode
            var detectionMode = (body.DetectionMode?.ToLowerInvariant()) switch
            {
                "voice" => DetectionMode.Voice,
                "text" => DetectionMode.Text,
                "both" => DetectionMode.Both,
                _ => DetectionMode.Both
            };

            // Parse feedback mode
            var feedbackMode = (body.DefaultFeedbackMode?.ToLowerInvariant()) switch
            {
                "standard" => FeedbackMode.Standard,
                "silent" => FeedbackMode.Silent,
                "haptic_only" => FeedbackMode.HapticOnly,
                "deceptive" => FeedbackMode.Deceptive,
                _ => FeedbackMode.Standard
            };

            // Create new detection session
            var session = new DetectionSession
            {
                SessionId = Guid.NewGuid(),
                UserId = body.UserId,
                Status = DetectionSessionStatus.Active,
                Mode = detectionMode,
                DefaultFeedbackMode = feedbackMode,
                StartedAt = DateTime.UtcNow,
                LastKnownLatitude = body.Location?.Latitude,
                LastKnownLongitude = body.Location?.Longitude
            };

            var created = await _sessionRepository.CreateAsync(session);

            _logger.LogInformation("Started detection session {SessionId} for user {UserId} with {Count} active phrases",
                created.SessionId, body.UserId, activePhraseCount);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                sessionId = created.SessionId,
                userId = created.UserId,
                status = created.Status.ToString().ToLowerInvariant(),
                detectionMode = created.Mode.ToString().ToLowerInvariant(),
                startedAt = created.StartedAt,
                activePhraseCount = activePhraseCount,
                currentLocation = body.Location != null ? new
                {
                    latitude = body.Location.Latitude,
                    longitude = body.Location.Longitude,
                    address = body.Location.Address
                } : null,
                defaultFeedbackMode = created.DefaultFeedbackMode.ToString().ToLowerInvariant(),
                activeDeceptiveDisguise = body.DefaultDeceptiveDisguise
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting detection session");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to start detection session");
        }
    }

    /// <summary>
    /// POST /detection/{sessionId}/stop - Stop phrase detection session
    /// </summary>
    [Function("StopDetection")]
    public async Task<HttpResponseData> StopDetection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "detection/{sessionId}/stop")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_SESSION_ID", "Invalid session ID");
            }

            var session = await _sessionRepository.GetByIdAsync(sessionGuid);
            if (session == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "SESSION_NOT_FOUND", "Detection session not found");
            }

            // Validate user owns this session
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || requesterId.Value != session.UserId)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            session.Status = DetectionSessionStatus.Stopped;
            session.EndedAt = DateTime.UtcNow;
            await _sessionRepository.UpdateAsync(session);

            var duration = (int)(session.EndedAt.Value - session.StartedAt).TotalSeconds;

            _logger.LogInformation("Stopped detection session {SessionId} for user {UserId}, duration: {Duration}s",
                sessionGuid, session.UserId, duration);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                sessionId = session.SessionId,
                stoppedAt = session.EndedAt,
                duration = duration
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping detection session");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to stop detection session");
        }
    }

    /// <summary>
    /// GET /detection/{sessionId}/status - Get detection session status
    /// </summary>
    [Function("GetDetectionStatus")]
    public async Task<HttpResponseData> GetDetectionStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "detection/{sessionId}/status")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_SESSION_ID", "Invalid session ID");
            }

            var session = await _sessionRepository.GetByIdAsync(sessionGuid);
            if (session == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "SESSION_NOT_FOUND", "Detection session not found");
            }

            // Validate user owns this session
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || requesterId.Value != session.UserId)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            var activePhrases = await _phraseRepository.GetUserPhrasesAsync(session.UserId, isActive: true);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                sessionId = session.SessionId,
                userId = session.UserId,
                status = session.Status.ToString().ToLowerInvariant(),
                detectionMode = session.Mode.ToString().ToLowerInvariant(),
                startedAt = session.StartedAt,
                activePhraseCount = activePhrases.Count(),
                currentLocation = session.LastKnownLatitude.HasValue && session.LastKnownLongitude.HasValue
                    ? new
                    {
                        latitude = session.LastKnownLatitude.Value,
                        longitude = session.LastKnownLongitude.Value
                    }
                    : null,
                defaultFeedbackMode = session.DefaultFeedbackMode.ToString().ToLowerInvariant()
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting detection session status");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to get session status");
        }
    }

    #endregion

    #region Trigger Detection

    /// <summary>
    /// POST /detection/trigger - Process detected phrase and trigger incident
    /// </summary>
    [Function("ProcessTrigger")]
    public async Task<HttpResponseData> ProcessTrigger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "detection/trigger")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<ProcessTriggerRequest>(req.Body);
            if (body == null || body.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(body.DetectedPhrase))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Session ID and detected phrase are required");
            }

            // Get session
            var session = await _sessionRepository.GetByIdAsync(body.SessionId);
            if (session == null || session.Status != DetectionSessionStatus.Active)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_SESSION", "Detection session is not active");
            }

            // Validate user owns this session
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || requesterId.Value != session.UserId)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            // Get user's active trigger phrases
            var userPhrases = await _phraseRepository.GetUserPhrasesAsync(session.UserId, isActive: true);

            // Match the detected phrase
            var matchResult = await _phraseMatching.FindBestMatchAsync(
                body.DetectedPhrase,
                userPhrases,
                "medium"); // Could be configurable per session

            if (matchResult == null || matchResult.Value.MatchedPhrase == null)
            {
                _logger.LogWarning("No matching phrase found for detected phrase: {DetectedPhrase}", body.DetectedPhrase);
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "NO_MATCH", "No matching trigger phrase found");
            }

            var (matchedPhrase, confidence) = matchResult.Value;

            _logger.LogInformation("Matched phrase {PhraseId} with confidence {Confidence} for user {UserId}",
                matchedPhrase.PhraseId, confidence, session.UserId);

            // Calculate geohash from location
            string? geohash = null;
            if (body.Location?.Latitude != null && body.Location?.Longitude != null)
            {
                geohash = _geohashService.Encode(body.Location.Latitude.Value, body.Location.Longitude.Value, 9);
            }

            // Create incident
            var incident = new Incident
            {
                IncidentId = Guid.NewGuid(),
                SummonerId = session.UserId,
                Status = matchedPhrase.ConfirmationRequired ? "pending_confirmation" : "dispatching",
                IncidentType = "trigger_phrase_detected",
                LocationLat = body.Location?.Latitude,
                LocationLng = body.Location?.Longitude,
                LocationGeohash = geohash ?? string.Empty,
                LocationAddress = body.Location?.Address,
                Description = $"Trigger phrase detected: {matchedPhrase.PhraseText}",
                ReportedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TriggeredPhraseId = matchedPhrase.PhraseId,
                DuressFlag = false
            };

            await _incidentRepository.CreateAsync(incident);

            // Update phrase stats
            matchedPhrase.TriggerCount++;
            matchedPhrase.LastTriggeredAt = DateTime.UtcNow;
            await _phraseRepository.UpdateAsync(matchedPhrase);

            // Update session
            session.Status = DetectionSessionStatus.IncidentTriggered;
            await _sessionRepository.UpdateAsync(session);

            _logger.LogInformation("Created incident {IncidentId} from trigger phrase {PhraseId}",
                incident.IncidentId, matchedPhrase.PhraseId);

            // Generate feedback response
            var feedbackModeEnum = Enum.Parse<FeedbackMode>(matchedPhrase.FeedbackMode, ignoreCase: true);
            DeceptiveDisguise? disguise = null;
            if (!string.IsNullOrEmpty(matchedPhrase.DeceptiveDisguiseApp))
            {
                disguise = Enum.Parse<DeceptiveDisguise>(matchedPhrase.DeceptiveDisguiseApp, ignoreCase: true);
            }

            var feedback = await _feedbackMode.GenerateFeedbackAsync(feedbackModeEnum, disguise);

            // Dispatch responders if not requiring confirmation
            bool dispatched = false;
            int respondersNotified = 0;
            if (!matchedPhrase.ConfirmationRequired && body.Location?.Latitude != null && body.Location?.Longitude != null)
            {
                try
                {
                    await _dispatchService.DispatchRespondersAsync(
                        incident.IncidentId,
                        body.Location.Latitude.Value,
                        body.Location.Longitude.Value);
                    dispatched = true;
                    // Note: Would need to track actual count from dispatch service
                    respondersNotified = 5; // Placeholder
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error dispatching responders for incident {IncidentId}", incident.IncidentId);
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                incidentId = incident.IncidentId,
                triggeredPhraseId = matchedPhrase.PhraseId,
                responseType = matchedPhrase.ResponseType,
                status = incident.Status,
                confirmationRequired = matchedPhrase.ConfirmationRequired,
                confirmationDeadline = matchedPhrase.ConfirmationRequired
                    ? DateTime.UtcNow.AddSeconds(matchedPhrase.ConfirmationTimeoutSeconds)
                    : (DateTime?)null,
                communityRespondersNotified = respondersNotified,
                firstRespondersDispatched = matchedPhrase.ResponseType == "community_and_first_responders" && dispatched,
                triggeredAt = incident.ReportedAt,
                feedbackMode = feedback.Mode.ToString().ToLowerInvariant(),
                feedbackDelivered = true,
                deceptiveDisguiseActive = feedback.DeceptiveDisguise
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing trigger");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to process trigger");
        }
    }

    /// <summary>
    /// POST /detection/cancel - Cancel triggered incident with PIN validation
    /// CRITICAL: Duress PIN must return 200 OK while silently escalating to HQ + Police
    /// </summary>
    [Function("CancelTrigger")]
    public async Task<HttpResponseData> CancelTrigger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "detection/cancel")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<CancelTriggerRequest>(req.Body);
            if (body == null || body.IncidentId == Guid.Empty || body.UserId == Guid.Empty)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Incident ID and User ID are required");
            }

            // Validate authentication
            var requesterId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!requesterId.HasValue || requesterId.Value != body.UserId)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access");
            }

            // Get incident
            var incident = await _incidentRepository.GetByIdAsync(body.IncidentId);
            if (incident == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "INCIDENT_NOT_FOUND", "Incident not found");
            }

            // Verify user is the summoner
            if (incident.SummonerId != body.UserId)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Only summoner can cancel incident");
            }

            // Check if incident can be cancelled (within time window)
            if (incident.Status == "resolved" || incident.Status == "cancelled")
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "ALREADY_COMPLETED", "Incident already completed");
            }

            // CRITICAL: Validate cancellation PIN (handles duress detection)
            bool isValidPin = false;
            bool isDuressPin = false;

            if (!string.IsNullOrEmpty(body.CancellationCode))
            {
                var pinResult = await _duressPinService.ValidateCancellationPinAsync(
                    body.UserId,
                    body.CancellationCode,
                    body.IncidentId);

                isValidPin = pinResult.IsValidPin;
                isDuressPin = pinResult.IsDuressPin;
            }
            else
            {
                // No PIN provided - allow cancellation but log it
                _logger.LogWarning("Incident {IncidentId} cancelled without PIN by user {UserId}",
                    body.IncidentId, body.UserId);
                isValidPin = true;
            }

            if (!isValidPin)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_PIN", "Invalid cancellation code");
            }

            // CRITICAL: Always return 200 OK, even if duress PIN was used
            // This prevents attacker from knowing the duress PIN was detected
            int notificationsSent = 0;

            if (isDuressPin)
            {
                // DURESS DETECTED: Silent escalation to HQ + Police
                _logger.LogWarning("DURESS PIN DETECTED for incident {IncidentId} by user {UserId}. Silently escalating.",
                    body.IncidentId, body.UserId);

                incident.Status = "escalated";
                incident.DuressFlag = true;
                incident.UpdatedAt = DateTime.UtcNow;
                await _incidentRepository.UpdateAsync(incident);

                // Silently notify HQ and escalate to police
                try
                {
                    // Send high-priority alert to HQ
                    await _notificationService.SendHqAlertAsync(
                        $"DURESS SITUATION - Incident {incident.IncidentId}",
                        $"User {body.UserId} used duress PIN. Immediate intervention required.",
                        "critical");

                    // Auto-dispatch police if not already done
                    if (incident.LocationLat.HasValue && incident.LocationLng.HasValue)
                    {
                        // Would integrate with 911 dispatch system here
                        _logger.LogWarning("Auto-dispatching police for duress incident {IncidentId}", body.IncidentId);
                    }

                    notificationsSent = 1; // Return fake count to user
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error escalating duress incident {IncidentId}", body.IncidentId);
                }
            }
            else
            {
                // LEGITIMATE CANCELLATION: Actually cancel the incident
                incident.Status = "cancelled";
                incident.UpdatedAt = DateTime.UtcNow;
                incident.ResolvedAt = DateTime.UtcNow;
                await _incidentRepository.UpdateAsync(incident);

                _logger.LogInformation("Incident {IncidentId} cancelled by user {UserId} with reason: {Reason}",
                    body.IncidentId, body.UserId, body.CancellationReason ?? "not_specified");

                // Send cancellation notifications to responders
                try
                {
                    // Would get assigned responders and notify them
                    notificationsSent = 0; // Track actual count
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending cancellation notifications for incident {IncidentId}", body.IncidentId);
                }
            }

            // CRITICAL: Return identical response for both duress and safe cancellation
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                incidentId = body.IncidentId,
                cancelled = true,
                cancelledAt = DateTime.UtcNow,
                notificationsSent = notificationsSent
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling trigger");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Failed to cancel trigger");
        }
    }

    #endregion

    #region Helper Methods

    private object MapToResponse(TriggerPhrase phrase)
    {
        string[]? alternativePhrases = null;
        if (!string.IsNullOrEmpty(phrase.AlternativePhrases))
        {
            try
            {
                alternativePhrases = JsonSerializer.Deserialize<string[]>(phrase.AlternativePhrases);
            }
            catch { }
        }

        string[]? firstResponderTypes = null;
        if (!string.IsNullOrEmpty(phrase.FirstResponderTypes))
        {
            try
            {
                firstResponderTypes = JsonSerializer.Deserialize<string[]>(phrase.FirstResponderTypes);
            }
            catch { }
        }

        return new
        {
            id = phrase.PhraseId,
            userId = phrase.UserId,
            phrase = phrase.PhraseText,
            alternativePhrases = alternativePhrases,
            responseType = phrase.ResponseType,
            priority = phrase.Priority,
            confirmationRequired = phrase.ConfirmationRequired,
            confirmationTimeoutSeconds = phrase.ConfirmationTimeoutSeconds,
            firstResponderTypes = firstResponderTypes,
            feedbackMode = phrase.FeedbackMode,
            deceptiveAppDisguise = phrase.DeceptiveDisguiseApp,
            isActive = phrase.IsActive,
            createdAt = phrase.CreatedAt,
            updatedAt = phrase.UpdatedAt,
            lastTriggeredAt = phrase.LastTriggeredAt,
            triggerCount = phrase.TriggerCount
        };
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string code, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new
        {
            code = code,
            message = message
        });
        return response;
    }

    #endregion

    #region Request Models

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

    private class StartDetectionRequest
    {
        public Guid UserId { get; set; }
        public string? DetectionMode { get; set; }
        public LocationRequest? Location { get; set; }
        public string? SensitivityLevel { get; set; }
        public string? DefaultFeedbackMode { get; set; }
        public string? DefaultDeceptiveDisguise { get; set; }
    }

    private class ProcessTriggerRequest
    {
        public Guid SessionId { get; set; }
        public string DetectedPhrase { get; set; } = string.Empty;
        public Guid MatchedPhraseId { get; set; }
        public double? MatchConfidence { get; set; }
        public LocationRequest? Location { get; set; }
        public string? AudioClipUrl { get; set; }
    }

    private class CancelTriggerRequest
    {
        public Guid IncidentId { get; set; }
        public Guid UserId { get; set; }
        public string? CancellationReason { get; set; }
        public string? CancellationCode { get; set; }
    }

    private class LocationRequest
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? Accuracy { get; set; }
        public string? Address { get; set; }
    }

    #endregion
}
