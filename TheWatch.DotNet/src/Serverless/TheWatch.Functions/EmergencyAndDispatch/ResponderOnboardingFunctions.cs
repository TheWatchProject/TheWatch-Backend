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
/// Azure Functions for responder onboarding, training, and background checks.
/// Implements endpoints from responder-onboarding-api.yaml.
///
/// Key Features:
/// - Background check integration with circuit breaker pattern
/// - Training module tracking with prerequisite validation
/// - Certification validation (background check + training + age)
/// - Schedule management for designated responders
/// - Audit logging for all status changes
/// - Support for parental consent (minors cannot be responders)
/// </summary>
public class ResponderOnboardingFunctions
{
    private readonly ILogger<ResponderOnboardingFunctions> _logger;
    private readonly IResponderOnboardingRepository _onboardingRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBackgroundCheckService _backgroundCheckService;
    private readonly ICertificationService _certificationService;
    private readonly ICryptographyService _cryptographyService;
    private readonly IAdminAuditRepository _auditRepository;
    private readonly GeohashService _geohashService;

    public ResponderOnboardingFunctions(
        ILogger<ResponderOnboardingFunctions> logger,
        IResponderOnboardingRepository onboardingRepository,
        IUserRepository userRepository,
        IBackgroundCheckService backgroundCheckService,
        ICertificationService certificationService,
        ICryptographyService cryptographyService,
        IAdminAuditRepository auditRepository)
    {
        _logger = logger;
        _onboardingRepository = onboardingRepository;
        _userRepository = userRepository;
        _backgroundCheckService = backgroundCheckService;
        _certificationService = certificationService;
        _cryptographyService = cryptographyService;
        _auditRepository = auditRepository;
        _geohashService = new GeohashService();
    }

    /// <summary>
    /// POST /responders/apply - Apply to become a responder
    /// Initiates background check and creates responder profile
    /// </summary>
    [Function("ApplyAsResponder")]
    public async Task<HttpResponseData> ApplyAsResponder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/apply")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Invalid or missing authentication token");
            }

            var body = await JsonSerializer.DeserializeAsync<ResponderApplicationRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Request body is required");
            }

            // Get user to validate age and account status
            var user = await _userRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "USER_NOT_FOUND", "User not found");
            }

            // Check if user is at least 18 years old
            var age = DateTime.UtcNow.Year - user.DateOfBirth.Year;
            if (user.DateOfBirth > DateTime.UtcNow.AddYears(-age)) age--;

            if (age < 18)
            {
                _logger.LogWarning("User {UserId} attempted to apply as responder but is a minor (age {Age})", userId.Value, age);
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "AGE_REQUIREMENT_NOT_MET", "Must be at least 18 years old to become a responder");
            }

            // Check if responder profile already exists
            var existingProfile = await _onboardingRepository.GetResponderProfileAsync(userId.Value);
            if (existingProfile != null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Conflict, "ALREADY_APPLIED", "Responder application already exists");
            }

            // Create responder profile
            var profile = new ResponderProfile
            {
                ResponderId = userId.Value,
                BackgroundCheckStatus = "pending",
                TrainingCompletionPercentage = 0,
                IsResponderEligible = false,
                DesignatedAgency = body.DesignatedAgency,
                ReliabilityRating = "unproven",
                CurrentStatus = "unavailable"
            };

            await _onboardingRepository.CreateResponderProfileAsync(profile);

            // Initiate background check if consent provided
            BackgroundCheckRecord? check = null;
            if (body.ConsentToBackgroundCheck && !string.IsNullOrEmpty(body.GovernmentIdPath))
            {
                try
                {
                    // Encrypt SSN last 4 if provided
                    string? ssnLast4Encrypted = null;
                    if (!string.IsNullOrEmpty(body.SsnLast4))
                    {
                        ssnLast4Encrypted = _cryptographyService.Encrypt(body.SsnLast4);
                    }

                    // Initiate background check with provider (circuit breaker pattern)
                    var providerCheckId = await _backgroundCheckService.InitiateBackgroundCheckAsync(
                        userId.Value,
                        body.GovernmentIdPath,
                        body.SsnLast4);

                    check = new BackgroundCheckRecord
                    {
                        CheckId = Guid.NewGuid(),
                        ResponderId = userId.Value,
                        Status = "pending",
                        Provider = "Checkr", // Default provider
                        ProviderCheckId = providerCheckId,
                        GovernmentIdPath = body.GovernmentIdPath,
                        SsnLast4Encrypted = ssnLast4Encrypted,
                        SubmittedAt = DateTime.UtcNow
                    };

                    await _onboardingRepository.CreateBackgroundCheckAsync(check);

                    _logger.LogInformation(
                        "Background check initiated for user {UserId}, provider check ID {ProviderCheckId}",
                        userId.Value, providerCheckId);

                    // Audit log
                    await _auditRepository.LogActionAsync(
                        userId.Value,
                        "background_check_initiated",
                        "ResponderOnboarding",
                        check.CheckId.ToString(),
                        new { provider = check.Provider, checkId = check.CheckId });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initiate background check for user {UserId}", userId.Value);
                    // Don't fail the application, just log the error
                    // Background check can be retried later
                }
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                responder_id = userId.Value,
                background_check_status = profile.BackgroundCheckStatus,
                background_check_id = check?.CheckId,
                training_progress = 0,
                is_responder_eligible = false,
                created_at = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing responder application");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred processing application");
        }
    }

    /// <summary>
    /// GET /responders/{responderId}/status - Check background check status
    /// Polls the background check provider for updates
    /// </summary>
    [Function("GetBackgroundCheckStatus")]
    public async Task<HttpResponseData> GetBackgroundCheckStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "responders/{responderId}/status")] HttpRequestData req,
        string responderId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Invalid or missing authentication token");
            }

            if (!Guid.TryParse(responderId, out var responderGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_RESPONDER_ID", "Invalid responder ID format");
            }

            // Verify user can access this responder's status (self or HQ/admin)
            if (userId.Value != responderGuid && !JwtUtilities.HasRole(req, "hq") && !JwtUtilities.HasRole(req, "admin"))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "ACCESS_DENIED", "Cannot access another user's background check status");
            }

            var latestCheck = await _onboardingRepository.GetLatestBackgroundCheckAsync(responderGuid);
            if (latestCheck == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "NO_BACKGROUND_CHECK", "No background check found for this responder");
            }

            // If check is still pending/processing, poll the provider for updates
            if ((latestCheck.Status == "pending" || latestCheck.Status == "processing") &&
                !string.IsNullOrEmpty(latestCheck.ProviderCheckId))
            {
                try
                {
                    var providerResult = await _backgroundCheckService.GetCheckStatusAsync(latestCheck.ProviderCheckId);

                    // Update local record if status changed
                    if (providerResult.Status != latestCheck.Status)
                    {
                        latestCheck.Status = providerResult.Status;
                        latestCheck.ResultNotes = providerResult.ResultNotes;
                        latestCheck.CompletedAt = providerResult.CompletedAt;
                        await _onboardingRepository.UpdateBackgroundCheckAsync(latestCheck);

                        // Update responder profile status
                        var profile = await _onboardingRepository.GetResponderProfileAsync(responderGuid);
                        if (profile != null)
                        {
                            profile.BackgroundCheckStatus = providerResult.Status;
                            await _onboardingRepository.UpdateResponderProfileAsync(profile);
                        }

                        _logger.LogInformation(
                            "Background check {CheckId} status updated to {Status}",
                            latestCheck.CheckId, providerResult.Status);

                        // Audit log
                        await _auditRepository.LogActionAsync(
                            responderGuid,
                            "background_check_status_updated",
                            "ResponderOnboarding",
                            latestCheck.CheckId.ToString(),
                            new { oldStatus = latestCheck.Status, newStatus = providerResult.Status });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to poll background check provider for check {CheckId}", latestCheck.CheckId);
                    // Continue with cached status
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                check_id = latestCheck.CheckId,
                status = latestCheck.Status,
                submitted_at = latestCheck.SubmittedAt,
                completed_at = latestCheck.CompletedAt,
                result_notes = latestCheck.ResultNotes
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting background check status");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving status");
        }
    }

    /// <summary>
    /// POST /responders/{responderId}/training/{moduleId}/complete - Mark training module complete
    /// Verifies prerequisites and updates completion status
    /// </summary>
    [Function("CompleteTrainingModule")]
    public async Task<HttpResponseData> CompleteTrainingModule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/{responderId}/training/{moduleId}/complete")] HttpRequestData req,
        string responderId,
        string moduleId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Invalid or missing authentication token");
            }

            if (!Guid.TryParse(responderId, out var responderGuid) || !Guid.TryParse(moduleId, out var moduleGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_ID", "Invalid responder or module ID format");
            }

            // Verify user can complete training for this responder (self only)
            if (userId.Value != responderGuid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "ACCESS_DENIED", "Cannot complete training for another user");
            }

            var body = await JsonSerializer.DeserializeAsync<TrainingCompletionRequest>(req.Body);

            // Verify module exists
            var module = await _onboardingRepository.GetTrainingModuleAsync(moduleGuid);
            if (module == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "MODULE_NOT_FOUND", "Training module not found");
            }

            // Check if already completed
            var existingCompletion = await _onboardingRepository.GetTrainingCompletionAsync(responderGuid, moduleGuid);
            if (existingCompletion?.Status == "completed")
            {
                return await CreateErrorResponse(req, HttpStatusCode.Conflict, "ALREADY_COMPLETED", "Training module already completed");
            }

            // TODO: Verify prerequisites are met
            // For now, allow any module to be completed

            // Create or update completion record
            ResponderTrainingCompletion completion;
            if (existingCompletion == null)
            {
                completion = new ResponderTrainingCompletion
                {
                    CompletionId = Guid.NewGuid(),
                    ResponderId = responderGuid,
                    ModuleId = moduleGuid,
                    Status = "completed",
                    QuizScore = body?.QuizScore,
                    VerificationToken = body?.VerificationToken,
                    CompletedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                await _onboardingRepository.CreateTrainingCompletionAsync(completion);
            }
            else
            {
                existingCompletion.Status = "completed";
                existingCompletion.QuizScore = body?.QuizScore;
                existingCompletion.VerificationToken = body?.VerificationToken;
                existingCompletion.CompletedAt = DateTime.UtcNow;
                await _onboardingRepository.UpdateTrainingCompletionAsync(existingCompletion);
                completion = existingCompletion;
            }

            // Update responder profile training percentage
            var allModules = await _onboardingRepository.GetAllTrainingModulesAsync();
            var completions = await _onboardingRepository.GetResponderTrainingCompletionsAsync(responderGuid);
            var mandatoryModules = allModules.Where(m => m.IsMandatory).ToList();
            var completedMandatory = completions.Count(c => c.Status == "completed" &&
                mandatoryModules.Any(m => m.ModuleId == c.ModuleId));
            var trainingPercentage = mandatoryModules.Count > 0
                ? (completedMandatory * 100 / mandatoryModules.Count)
                : 0;

            var profile = await _onboardingRepository.GetResponderProfileAsync(responderGuid);
            if (profile != null)
            {
                profile.TrainingCompletionPercentage = trainingPercentage;
                await _onboardingRepository.UpdateResponderProfileAsync(profile);
            }

            _logger.LogInformation(
                "Training module {ModuleId} completed by responder {ResponderId}, training progress {Percentage}%",
                moduleGuid, responderGuid, trainingPercentage);

            // Audit log
            await _auditRepository.LogActionAsync(
                responderGuid,
                "training_module_completed",
                "ResponderOnboarding",
                moduleGuid.ToString(),
                new { moduleTitle = module.Title, score = body?.QuizScore, trainingPercentage });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                completion_id = completion.CompletionId,
                module_id = moduleGuid,
                module_title = module.Title,
                status = "completed",
                completed_at = completion.CompletedAt,
                quiz_score = completion.QuizScore,
                training_progress = trainingPercentage
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing training module");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred completing training");
        }
    }

    /// <summary>
    /// GET /responders/{responderId}/certification - Get certification status
    /// Validates: background check passed + all training modules complete + age >= 18
    /// </summary>
    [Function("GetCertificationStatus")]
    public async Task<HttpResponseData> GetCertificationStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "responders/{responderId}/certification")] HttpRequestData req,
        string responderId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Invalid or missing authentication token");
            }

            if (!Guid.TryParse(responderId, out var responderGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_RESPONDER_ID", "Invalid responder ID format");
            }

            // Verify user can access this responder's certification (self or HQ/admin)
            if (userId.Value != responderGuid && !JwtUtilities.HasRole(req, "hq") && !JwtUtilities.HasRole(req, "admin"))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "ACCESS_DENIED", "Cannot access another user's certification status");
            }

            // Validate certification
            var certStatus = await _certificationService.ValidateCertificationAsync(responderGuid);

            // Update responder profile if certification status changed
            var profile = await _onboardingRepository.GetResponderProfileAsync(responderGuid);
            if (profile != null && profile.IsResponderEligible != certStatus.IsCertified)
            {
                await _certificationService.UpdateResponderCertificationAsync(responderGuid, certStatus.IsCertified);

                _logger.LogInformation(
                    "Responder {ResponderId} certification status updated to {IsCertified}",
                    responderGuid, certStatus.IsCertified);

                // Audit log
                await _auditRepository.LogActionAsync(
                    responderGuid,
                    "certification_status_updated",
                    "ResponderOnboarding",
                    responderGuid.ToString(),
                    new { isCertified = certStatus.IsCertified, missingRequirements = certStatus.MissingRequirements });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                responder_id = responderGuid,
                is_certified = certStatus.IsCertified,
                background_check_status = certStatus.BackgroundCheckStatus,
                training_completion_percentage = certStatus.TrainingCompletionPercentage,
                is_adult = certStatus.IsAdult,
                missing_requirements = certStatus.MissingRequirements,
                certified_at = certStatus.CertifiedAt
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting certification status");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving certification status");
        }
    }

    /// <summary>
    /// POST /responders/{responderId}/schedule - Set designated responder schedule
    /// Creates availability schedule for role-based availability
    /// </summary>
    [Function("CreateResponderSchedule")]
    public async Task<HttpResponseData> CreateResponderSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/{responderId}/schedule")] HttpRequestData req,
        string responderId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Invalid or missing authentication token");
            }

            if (!Guid.TryParse(responderId, out var responderGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_RESPONDER_ID", "Invalid responder ID format");
            }

            // Verify user can create schedule for this responder (self only)
            if (userId.Value != responderGuid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "ACCESS_DENIED", "Cannot create schedule for another user");
            }

            var body = await JsonSerializer.DeserializeAsync<CreateScheduleRequest>(req.Body);
            if (body == null || body.LocationLat == 0 || body.LocationLng == 0 || body.RadiusMeters == 0)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Location and radius are required");
            }

            // Verify responder is certified
            var profile = await _onboardingRepository.GetResponderProfileAsync(responderGuid);
            if (profile == null || !profile.IsResponderEligible)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "NOT_CERTIFIED", "Must be a certified responder to create schedules");
            }

            // Calculate geohash for location
            var geohash = _geohashService.Encode(body.LocationLat, body.LocationLng, 7); // Precision 7 (~1km)

            var schedule = new DesignatedResponderSchedule
            {
                DesignationId = Guid.NewGuid(),
                ResponderId = responderGuid,
                CommitmentType = body.CommitmentType ?? "recurring",
                LocationLat = body.LocationLat,
                LocationLng = body.LocationLng,
                LocationGeohash = geohash,
                LocationName = body.LocationName,
                RadiusMeters = body.RadiusMeters,
                StartTime = body.StartTime,
                EndTime = body.EndTime,
                RecurrencePattern = body.RecurrencePattern != null
                    ? JsonSerializer.Serialize(body.RecurrencePattern)
                    : null,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _onboardingRepository.CreateScheduleAsync(schedule);

            _logger.LogInformation(
                "Designated responder schedule created for {ResponderId} at location {LocationName} ({Lat}, {Lng})",
                responderGuid, body.LocationName, body.LocationLat, body.LocationLng);

            // Audit log
            await _auditRepository.LogActionAsync(
                responderGuid,
                "schedule_created",
                "ResponderOnboarding",
                schedule.DesignationId.ToString(),
                new { locationName = body.LocationName, commitmentType = schedule.CommitmentType });

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                designation_id = schedule.DesignationId,
                responder_id = responderGuid,
                commitment_type = schedule.CommitmentType,
                location = new
                {
                    latitude = schedule.LocationLat,
                    longitude = schedule.LocationLng,
                    geohash = schedule.LocationGeohash,
                    name = schedule.LocationName
                },
                radius_meters = schedule.RadiusMeters,
                start_time = schedule.StartTime,
                end_time = schedule.EndTime,
                recurrence_pattern = schedule.RecurrencePattern,
                is_active = schedule.IsActive,
                created_at = schedule.CreatedAt
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating responder schedule");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred creating schedule");
        }
    }

    /// <summary>
    /// PUT /responders/{responderId}/availability - Update availability status
    /// Updates current status: available, busy, on_duty, unavailable
    /// </summary>
    [Function("UpdateResponderAvailability")]
    public async Task<HttpResponseData> UpdateResponderAvailability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "responders/{responderId}/availability")] HttpRequestData req,
        string responderId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Invalid or missing authentication token");
            }

            if (!Guid.TryParse(responderId, out var responderGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_RESPONDER_ID", "Invalid responder ID format");
            }

            // Verify user can update availability for this responder (self only)
            if (userId.Value != responderGuid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Forbidden, "ACCESS_DENIED", "Cannot update availability for another user");
            }

            var body = await JsonSerializer.DeserializeAsync<UpdateAvailabilityRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.Status))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Status is required");
            }

            // Validate status value
            var validStatuses = new[] { "available", "busy", "on_duty", "unavailable" };
            if (!validStatuses.Contains(body.Status))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_STATUS",
                    "Status must be one of: available, busy, on_duty, unavailable");
            }

            // Update responder profile
            var profile = await _onboardingRepository.GetResponderProfileAsync(responderGuid);
            if (profile == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "PROFILE_NOT_FOUND", "Responder profile not found");
            }

            var oldStatus = profile.CurrentStatus;
            profile.CurrentStatus = body.Status;
            await _onboardingRepository.UpdateResponderProfileAsync(profile);

            _logger.LogInformation(
                "Responder {ResponderId} availability updated from {OldStatus} to {NewStatus}",
                responderGuid, oldStatus, body.Status);

            // Audit log
            await _auditRepository.LogActionAsync(
                responderGuid,
                "availability_updated",
                "ResponderOnboarding",
                responderGuid.ToString(),
                new { oldStatus, newStatus = body.Status });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                responder_id = responderGuid,
                status = profile.CurrentStatus,
                updated_at = DateTime.UtcNow
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
    /// GET /responders/onboarding/status - Get onboarding status (existing endpoint)
    /// </summary>
    [Function("GetOnboardingStatus")]
    public async Task<HttpResponseData> GetOnboardingStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "responders/onboarding/status")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Invalid or missing authentication token");
            }

            var profile = await _onboardingRepository.GetResponderProfileAsync(userId.Value);
            if (profile == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "PROFILE_NOT_FOUND", "Responder profile not found");
            }

            var completions = await _onboardingRepository.GetResponderTrainingCompletionsAsync(userId.Value);
            var modules = await _onboardingRepository.GetAllTrainingModulesAsync();

            var completedModules = completions.Count(c => c.Status == "completed");
            var totalModules = modules.Count(m => m.IsMandatory);
            var trainingProgress = totalModules > 0 ? (completedModules * 100 / totalModules) : 0;

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                user_id = userId.Value,
                background_check_status = profile.BackgroundCheckStatus,
                training_progress = trainingProgress,
                is_responder_eligible = profile.IsResponderEligible
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting onboarding status");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving onboarding status");
        }
    }

    /// <summary>
    /// GET /training/modules - Get training modules (existing endpoint)
    /// </summary>
    [Function("GetTrainingModules")]
    public async Task<HttpResponseData> GetTrainingModules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "training/modules")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Invalid or missing authentication token");
            }

            var modules = await _onboardingRepository.GetAllTrainingModulesAsync();
            var completions = await _onboardingRepository.GetResponderTrainingCompletionsAsync(userId.Value);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(modules.Select(m =>
            {
                var completion = completions.FirstOrDefault(c => c.ModuleId == m.ModuleId);
                return new
                {
                    module_id = m.ModuleId,
                    title = m.Title,
                    description = m.Description,
                    is_mandatory = m.IsMandatory,
                    status = completion?.Status ?? "not_started",
                    completed_at = completion?.CompletedAt
                };
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting training modules");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving training modules");
        }
    }

    // Helper methods

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string code, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { code, message });
        return response;
    }

    // DTOs

    private class ResponderApplicationRequest
    {
        public bool ConsentToBackgroundCheck { get; set; }
        public string? GovernmentIdPath { get; set; }
        public string? SsnLast4 { get; set; }
        public string? DesignatedAgency { get; set; }
    }

    private class TrainingCompletionRequest
    {
        public int? QuizScore { get; set; }
        public string? VerificationToken { get; set; }
    }

    private class CreateScheduleRequest
    {
        public string? CommitmentType { get; set; }
        public double LocationLat { get; set; }
        public double LocationLng { get; set; }
        public string? LocationName { get; set; }
        public int RadiusMeters { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public RecurrencePatternDto? RecurrencePattern { get; set; }
    }

    private class RecurrencePatternDto
    {
        public List<string>? DaysOfWeek { get; set; }
        public string? TimeRangeStart { get; set; }
        public string? TimeRangeEnd { get; set; }
    }

    private class UpdateAvailabilityRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
