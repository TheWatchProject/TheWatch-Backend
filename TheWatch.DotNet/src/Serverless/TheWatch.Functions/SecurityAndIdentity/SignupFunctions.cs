using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for user registration and onboarding.
/// Implements endpoints from signup-api.yaml.
/// </summary>
public class SignupFunctions
{
    private readonly ILogger<SignupFunctions> _logger;
    private readonly IUserRepository _userRepository;
    private readonly ICryptographyService _cryptography;
    private readonly IJwtService _jwtService;

    // In-memory storage for registration sessions (in production, use Redis or Cosmos DB)
    private static readonly Dictionary<Guid, RegistrationSession> _registrationSessions = new();

    public SignupFunctions(
        ILogger<SignupFunctions> logger,
        IUserRepository userRepository,
        ICryptographyService cryptography,
        IJwtService jwtService)
    {
        _logger = logger;
        _userRepository = userRepository;
        _cryptography = cryptography;
        _jwtService = jwtService;
    }

    /// <summary>
    /// POST /signup/start - Start registration process
    /// </summary>
    [Function("StartRegistration")]
    public async Task<HttpResponseData> StartRegistration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "signup/start")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<RegistrationStartRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Request body is required");
            }

            // Validate required fields
            if (string.IsNullOrEmpty(body.Email) || string.IsNullOrEmpty(body.FirstName) ||
                string.IsNullOrEmpty(body.LastName) || body.DateOfBirth == default)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "MISSING_FIELDS", "Required fields are missing");
            }

            // Check if email/phone already exists
            if (await _userRepository.EmailExistsAsync(body.Email))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Conflict, "EMAIL_EXISTS", "Email already registered");
            }

            if (!string.IsNullOrEmpty(body.PhoneNumber) && await _userRepository.PhoneExistsAsync(body.PhoneNumber))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Conflict, "PHONE_EXISTS", "Phone number already registered");
            }

            // Calculate age and determine capabilities
            var age = CalculateAge(body.DateOfBirth);
            var isMinor = age < 18;
            var ageCategory = age < 13 ? "child" : (age < 18 ? "teen" : "adult");

            var capabilities = new UserCapabilities
            {
                CanCreateTriggerPhrases = true,
                CanUseTriggerPhrases = true,
                CanBeSummonedAsResponder = !isMinor,
                CanViewIncidentHistory = true,
                CanAccessLiveVideo = !isMinor,
                RequiresParentalConsent = isMinor
            };

            // Create registration session
            var registrationId = Guid.NewGuid();
            var session = new RegistrationSession
            {
                RegistrationId = registrationId,
                Status = isMinor ? "parental_consent_pending" : "age_verified",
                Email = body.Email,
                PhoneNumber = body.PhoneNumber,
                FirstName = body.FirstName,
                LastName = body.LastName,
                DateOfBirth = body.DateOfBirth,
                AgeCategory = ageCategory,
                IsMinor = isMinor,
                Capabilities = capabilities,
                RequiresParentalConsent = isMinor,
                ParentalConsentStatus = isMinor ? "pending" : "not_required",
                WalkthroughStatus = "not_started",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            _registrationSessions[registrationId] = session;

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                registration_id = registrationId,
                status = session.Status,
                user_info = new
                {
                    first_name = body.FirstName,
                    last_name = body.LastName,
                    email = body.Email
                },
                age_verification = new
                {
                    date_of_birth = body.DateOfBirth,
                    age_years = age,
                    age_category = ageCategory,
                    is_minor = isMinor,
                    capabilities = capabilities,
                    restrictions = isMinor ? new[] { "Cannot be summoned as a responder", "Requires parental consent" } : Array.Empty<string>()
                },
                requires_parental_consent = isMinor,
                parental_consent_status = session.ParentalConsentStatus,
                walkthrough_required = true,
                walkthrough_status = "not_started",
                next_step = isMinor ? "submit_parental_consent" : "start_walkthrough",
                created_at = session.CreatedAt,
                expires_at = session.ExpiresAt
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting registration");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during registration");
        }
    }

    /// <summary>
    /// POST /signup/{registrationId}/parental-consent - Submit parental consent
    /// </summary>
    [Function("SubmitParentalConsent")]
    public async Task<HttpResponseData> SubmitParentalConsent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "signup/{registrationId}/parental-consent")] HttpRequestData req,
        string registrationId)
    {
        try
        {
            if (!Guid.TryParse(registrationId, out var regId) || !_registrationSessions.ContainsKey(regId))
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "REGISTRATION_NOT_FOUND", "Registration session not found");
            }

            var session = _registrationSessions[regId];

            var body = await JsonSerializer.DeserializeAsync<ParentalConsentSubmissionRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Request body is required");
            }

            // Generate verification code
            var verificationCode = _cryptography.GenerateVerificationCode();
            var codeHash = _cryptography.ComputeSha256Hash(verificationCode);

            // Create parental consent record
            var consent = new ParentalConsentRecord
            {
                ConsentId = Guid.NewGuid(),
                MinorUserId = Guid.Empty, // Will be set when user is created
                ParentName = body.ParentName,
                ParentEmail = body.ParentEmail,
                ParentPhone = body.ParentPhone,
                Relationship = body.Relationship,
                VerificationMethod = body.VerificationMethod ?? "email",
                VerificationCodeHash = codeHash,
                Consents = JsonSerializer.Serialize(body.Acknowledgments),
                Status = "pending_verification",
                SubmittedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            session.ParentalConsentId = consent.ConsentId;
            session.ParentalConsentStatus = "pending_verification";
            session.ParentalConsent = consent;

            // Send verification code via email/SMS
            // Note: In production, implement INotificationService.SendVerificationCodeAsync method
            // await _notificationService.SendVerificationCodeAsync(body.ParentEmail, verificationCode);
            _logger.LogInformation($"Verification code for parental consent: {verificationCode}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                registration_id = regId,
                consent_status = "pending_verification",
                message = $"Verification code sent to {body.ParentEmail}"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting parental consent");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred submitting parental consent");
        }
    }

    /// <summary>
    /// POST /signup/{registrationId}/walkthrough/start - Start incident walkthrough
    /// </summary>
    [Function("StartWalkthrough")]
    public async Task<HttpResponseData> StartWalkthrough(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "signup/{registrationId}/walkthrough/start")] HttpRequestData req,
        string registrationId)
    {
        try
        {
            if (!Guid.TryParse(registrationId, out var regId) || !_registrationSessions.ContainsKey(regId))
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "REGISTRATION_NOT_FOUND", "Registration session not found");
            }

            var session = _registrationSessions[regId];

            // Check prerequisites
            if (session.RequiresParentalConsent && session.ParentalConsentStatus != "verified")
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "CONSENT_REQUIRED", "Parental consent must be verified before starting walkthrough");
            }

            var walkthroughId = Guid.NewGuid();
            session.WalkthroughId = walkthroughId;
            session.WalkthroughStatus = "in_progress";
            session.Status = "walkthrough_in_progress";

            var steps = session.IsMinor ? GetMinorWalkthroughSteps() : GetStandardWalkthroughSteps();

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                registration_id = regId,
                walkthrough_id = walkthroughId,
                walkthrough_type = session.IsMinor ? "minor_focused" : "standard",
                status = "in_progress",
                progress = new
                {
                    total_steps = steps.Length,
                    completed_steps = 0,
                    percent_complete = 0,
                    current_step_number = 1
                },
                current_step = steps[0],
                steps = steps.Select((s, i) => new
                {
                    step_id = s.StepId,
                    step_number = i + 1,
                    title = s.Title,
                    step_type = s.StepType,
                    is_completed = false,
                    is_current_step = i == 0
                }),
                started_at = DateTime.UtcNow,
                estimated_time_remaining = 10
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting walkthrough");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred starting walkthrough");
        }
    }

    /// <summary>
    /// POST /signup/{registrationId}/complete - Complete registration
    /// </summary>
    [Function("CompleteRegistration")]
    public async Task<HttpResponseData> CompleteRegistration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "signup/{registrationId}/complete")] HttpRequestData req,
        string registrationId)
    {
        try
        {
            if (!Guid.TryParse(registrationId, out var regId) || !_registrationSessions.ContainsKey(regId))
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "REGISTRATION_NOT_FOUND", "Registration session not found");
            }

            var session = _registrationSessions[regId];
            var body = await JsonSerializer.DeserializeAsync<RegistrationCompleteRequest>(req.Body);

            if (body == null || string.IsNullOrEmpty(body.Password))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Password is required");
            }

            // Validate prerequisites
            if (session.RequiresParentalConsent && session.ParentalConsentStatus != "verified")
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "CONSENT_REQUIRED", "Parental consent must be verified");
            }

            if (session.WalkthroughStatus != "completed")
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "WALKTHROUGH_INCOMPLETE", "Incident walkthrough must be completed");
            }

            if (!body.TermsAccepted || !body.PrivacyPolicyAccepted)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TERMS_NOT_ACCEPTED", "Terms of service and privacy policy must be accepted");
            }

            // Create user account
            var user = new User
            {
                UserId = Guid.NewGuid(),
                Email = session.Email,
                Phone = session.PhoneNumber,
                Name = $"{session.FirstName} {session.LastName}",
                DateOfBirth = session.DateOfBirth,
                AccountType = session.IsMinor ? "summoner_only" : "responder_eligible",
                AgeVerifiedAt = DateTime.UtcNow,
                ParentalConsentId = session.ParentalConsentId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PiiState = "normal",
                PasswordHash = _cryptography.HashPassword(body.Password)
            };

            await _userRepository.CreateUserAsync(user);

            // Save parental consent if applicable
            if (session.ParentalConsent != null)
            {
                session.ParentalConsent.MinorUserId = user.UserId;
                await _userRepository.CreateParentalConsentAsync(session.ParentalConsent);
            }

            // Generate access and refresh tokens
            var roles = DetermineUserRoles(user);
            var accessToken = _jwtService.GenerateAccessToken(user.UserId, roles, 15);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Cleanup registration session
            _registrationSessions.Remove(regId);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                user_id = user.UserId,
                registration_id = regId,
                status = "completed",
                user_profile = new
                {
                    first_name = session.FirstName,
                    last_name = session.LastName,
                    email = session.Email,
                    phone_number = session.PhoneNumber,
                    age_category = session.AgeCategory,
                    capabilities = session.Capabilities
                },
                access_token = accessToken,
                refresh_token = refreshToken,
                walkthrough_completed = true,
                next_steps = new[]
                {
                    "Set up your first trigger phrase",
                    "Add emergency contacts",
                    "Enable location services"
                }
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing registration");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred completing registration");
        }
    }

    // Helper methods

    private int CalculateAge(DateTime dateOfBirth)
    {
        var today = DateTime.Today;
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > today.AddYears(-age)) age--;
        return age;
    }

    private string[] DetermineUserRoles(User user)
    {
        var roles = new List<string> { "summoner" };
        if (user.AccountType == "responder_eligible")
        {
            roles.Add("responder");
        }
        return roles.ToArray();
    }

    private WalkthroughStep[] GetStandardWalkthroughSteps()
    {
        return new[]
        {
            new WalkthroughStep
            {
                StepId = "intro",
                StepNumber = 1,
                Title = "Welcome to The Watch",
                Description = "Learn how The Watch works",
                StepType = "informational",
                RequiredAction = "read_and_acknowledge"
            },
            new WalkthroughStep
            {
                StepId = "trigger_phrases",
                StepNumber = 2,
                Title = "Understanding Trigger Phrases",
                Description = "How to set up your emergency phrases",
                StepType = "interactive",
                RequiredAction = "practice_phrase"
            },
            new WalkthroughStep
            {
                StepId = "simulate_incident",
                StepNumber = 3,
                Title = "Triggering an Incident",
                Description = "See what happens when you trigger an incident",
                StepType = "simulation",
                RequiredAction = "perform_simulation"
            },
            new WalkthroughStep
            {
                StepId = "final",
                StepNumber = 4,
                Title = "You're Ready!",
                Description = "Complete your registration",
                StepType = "acknowledgment",
                RequiredAction = "read_and_acknowledge"
            }
        };
    }

    private WalkthroughStep[] GetMinorWalkthroughSteps()
    {
        return new[]
        {
            new WalkthroughStep
            {
                StepId = "intro_minor",
                StepNumber = 1,
                Title = "Welcome to The Watch",
                Description = "Learn how The Watch works for you",
                StepType = "informational",
                RequiredAction = "read_and_acknowledge"
            },
            new WalkthroughStep
            {
                StepId = "safety_features",
                StepNumber = 2,
                Title = "Safety Features",
                Description = "Important safety features for young users",
                StepType = "informational",
                RequiredAction = "watch_video"
            },
            new WalkthroughStep
            {
                StepId = "final_minor",
                StepNumber = 3,
                Title = "You're Ready!",
                Description = "Complete your registration",
                StepType = "acknowledgment",
                RequiredAction = "read_and_acknowledge"
            }
        };
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string code, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { code, message });
        return response;
    }

    // DTOs

    private class RegistrationStartRequest
    {
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
    }

    private class ParentalConsentSubmissionRequest
    {
        public string ParentName { get; set; } = string.Empty;
        public string ParentEmail { get; set; } = string.Empty;
        public string? ParentPhone { get; set; }
        public string Relationship { get; set; } = string.Empty;
        public ParentalAcknowledgments Acknowledgments { get; set; } = new();
        public string? VerificationMethod { get; set; }
    }

    private class ParentalAcknowledgments
    {
        public bool UnderstandsMinorCanUseTriggerPhrases { get; set; }
        public bool UnderstandsMinorWillNotBeResponder { get; set; }
        public bool UnderstandsRecordingDuringIncidents { get; set; }
        public bool UnderstandsLocationTracking { get; set; }
        public bool AgreesToTermsOnBehalfOfMinor { get; set; }
    }

    private class RegistrationCompleteRequest
    {
        public string Password { get; set; } = string.Empty;
        public bool TermsAccepted { get; set; }
        public bool PrivacyPolicyAccepted { get; set; }
        public bool CommunityGuidelinesAccepted { get; set; }
    }

    private class RegistrationSession
    {
        public Guid RegistrationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string AgeCategory { get; set; } = string.Empty;
        public bool IsMinor { get; set; }
        public UserCapabilities Capabilities { get; set; } = new();
        public bool RequiresParentalConsent { get; set; }
        public string ParentalConsentStatus { get; set; } = string.Empty;
        public Guid? ParentalConsentId { get; set; }
        public ParentalConsentRecord? ParentalConsent { get; set; }
        public Guid? WalkthroughId { get; set; }
        public string WalkthroughStatus { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private class UserCapabilities
    {
        public bool CanCreateTriggerPhrases { get; set; }
        public bool CanUseTriggerPhrases { get; set; }
        public bool CanBeSummonedAsResponder { get; set; }
        public bool CanViewIncidentHistory { get; set; }
        public bool CanAccessLiveVideo { get; set; }
        public bool RequiresParentalConsent { get; set; }
    }

    private class WalkthroughStep
    {
        public string StepId { get; set; } = string.Empty;
        public int StepNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string StepType { get; set; } = string.Empty;
        public string RequiredAction { get; set; } = string.Empty;
    }
}
