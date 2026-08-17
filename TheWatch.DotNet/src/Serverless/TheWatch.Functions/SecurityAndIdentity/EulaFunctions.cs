using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for legal agreements and user consent management.
/// Implements endpoints from eula-api.yaml.
/// </summary>
public class EulaFunctions
{
    private readonly ILogger<EulaFunctions> _logger;
    private readonly ILegalAgreementRepository _agreementRepository;
    private readonly IUserRepository _userRepository;

    public EulaFunctions(
        ILogger<EulaFunctions> logger,
        ILegalAgreementRepository agreementRepository,
        IUserRepository userRepository)
    {
        _logger = logger;
        _agreementRepository = agreementRepository;
        _userRepository = userRepository;
    }

    /// <summary>
    /// GET /agreements - Get all current legal agreements
    /// </summary>
    [Function("GetAllAgreements")]
    public async Task<HttpResponseData> GetAllAgreements(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agreements")] HttpRequestData req)
    {
        try
        {
            var agreements = await _agreementRepository.GetCurrentAgreementsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                agreements = agreements.Select(a => new
                {
                    type = a.AgreementType,
                    title = GetAgreementTitle(a.AgreementType),
                    version = a.Version,
                    effective_date = a.EffectiveDate,
                    is_required = a.IsRequired,
                    applies_to = JsonSerializer.Deserialize<string[]>(a.AppliesTo),
                    summary_url = a.ContentUrl,
                    full_document_url = a.ContentUrl
                }),
                last_updated = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agreements");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving agreements");
        }
    }

    /// <summary>
    /// GET /agreements/{agreementType} - Get a specific agreement
    /// </summary>
    [Function("GetAgreement")]
    public async Task<HttpResponseData> GetAgreement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agreements/{agreementType}")] HttpRequestData req,
        string agreementType)
    {
        try
        {
            var agreement = await _agreementRepository.GetLatestAgreementByTypeAsync(agreementType);
            if (agreement == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "AGREEMENT_NOT_FOUND", "Agreement not found");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                type = agreement.AgreementType,
                title = GetAgreementTitle(agreement.AgreementType),
                version = agreement.Version,
                effective_date = agreement.EffectiveDate,
                language = "en",
                content = new
                {
                    full_text = $"Full content of {agreement.AgreementType}...",
                    summary = $"Summary of {agreement.AgreementType}",
                    key_points = GetKeyPoints(agreement.AgreementType)
                },
                is_required = agreement.IsRequired,
                applies_to = JsonSerializer.Deserialize<string[]>(agreement.AppliesTo),
                last_updated = agreement.CreatedAt
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agreement");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving agreement");
        }
    }

    /// <summary>
    /// GET /users/{userId}/consents - Get all consents for a user
    /// </summary>
    [Function("GetUserConsents")]
    public async Task<HttpResponseData> GetUserConsents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/consents")] HttpRequestData req,
        string userId)
    {
        try
        {
            if (!Guid.TryParse(userId, out var userGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_USER_ID", "Invalid user ID");
            }

            var user = await _userRepository.GetUserByIdAsync(userGuid);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "USER_NOT_FOUND", "User not found");
            }

            var consents = await _userRepository.GetUserConsentsAsync(userGuid);
            var allAgreements = await _agreementRepository.GetCurrentAgreementsAsync();

            var allRequiredConsentsGiven = allAgreements
                .Where(a => a.IsRequired)
                .All(a => consents.Any(c => c.AgreementId == a.AgreementId));

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                user_id = userGuid,
                user_type = user.DateOfBirth >= DateTime.UtcNow.AddYears(-18) ? "minor" : "adult",
                all_required_consents_given = allRequiredConsentsGiven,
                consents = consents.Select(c => new
                {
                    id = c.ConsentId,
                    user_id = c.UserId,
                    agreement_type = c.Agreement.AgreementType,
                    agreement_version = c.Agreement.Version,
                    accepted = true,
                    consented_at = c.AcceptedAt,
                    consent_method = "checkbox",
                    ip_address = c.IpAddress,
                    is_current_version = true,
                    requires_re_consent = false
                }),
                pending_consents = new string[] { },
                account_status = allRequiredConsentsGiven ? "active" : "pending_consent"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user consents");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving consents");
        }
    }

    /// <summary>
    /// POST /users/{userId}/consents - Record user consent
    /// </summary>
    [Function("RecordUserConsent")]
    public async Task<HttpResponseData> RecordUserConsent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{userId}/consents")] HttpRequestData req,
        string userId)
    {
        try
        {
            if (!Guid.TryParse(userId, out var userGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_USER_ID", "Invalid user ID");
            }

            var body = await JsonSerializer.DeserializeAsync<ConsentSubmissionRequest>(req.Body);
            if (body == null || body.Agreements == null || body.Agreements.Length == 0)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Agreements are required");
            }

            var ipAddress = req.Headers.TryGetValues("X-Forwarded-For", out var values)
                ? values.First()
                : "unknown";

            var consents = new List<UserAgreementConsent>();

            foreach (var agreementConsent in body.Agreements)
            {
                var agreement = await _agreementRepository.GetAgreementByTypeAndVersionAsync(
                    agreementConsent.AgreementType,
                    agreementConsent.Version);

                if (agreement == null)
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "AGREEMENT_NOT_FOUND", $"Agreement {agreementConsent.AgreementType} version {agreementConsent.Version} not found");
                }

                var consent = new UserAgreementConsent
                {
                    ConsentId = Guid.NewGuid(),
                    UserId = userGuid,
                    AgreementId = agreement.AgreementId,
                    AcceptedAt = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    UserAgent = req.Headers.TryGetValues("User-Agent", out var ua) ? ua.First() : null
                };

                await _userRepository.CreateConsentAsync(consent);
                consents.Add(consent);
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                id = consents.First().ConsentId,
                user_id = userGuid,
                agreement_type = consents.First().Agreement.AgreementType,
                agreement_version = consents.First().Agreement.Version,
                accepted = true,
                consented_at = DateTime.UtcNow,
                consent_method = body.ConsentMethod ?? "checkbox",
                ip_address = ipAddress,
                is_current_version = true,
                requires_re_consent = false
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording user consent");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred recording consent");
        }
    }

    /// <summary>
    /// GET /users/{userId}/parental-consent - Get parental consent status
    /// </summary>
    [Function("GetParentalConsentStatus")]
    public async Task<HttpResponseData> GetParentalConsentStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/parental-consent")] HttpRequestData req,
        string userId)
    {
        try
        {
            if (!Guid.TryParse(userId, out var userGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_USER_ID", "Invalid user ID");
            }

            var consent = await _userRepository.GetParentalConsentAsync(userGuid);
            if (consent == null)
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    registration_id = userGuid,
                    required = false,
                    status = "not_required"
                });
                return response;
            }

            var statusResponse = req.CreateResponse(HttpStatusCode.OK);
            await statusResponse.WriteAsJsonAsync(new
            {
                registration_id = userGuid,
                required = true,
                status = consent.Status,
                parent_email = consent.ParentEmail,
                verification_sent_at = consent.SubmittedAt,
                verified_at = consent.VerifiedAt,
                expires_at = consent.ExpiresAt
            });

            return statusResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parental consent status");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving parental consent");
        }
    }

    // Helper methods

    private string GetAgreementTitle(string type)
    {
        return type switch
        {
            "terms_of_service" => "Terms of Service",
            "privacy_policy" => "Privacy Policy",
            "community_guidelines" => "Community Guidelines",
            "incident_recording_consent" => "Incident Recording Consent",
            "location_tracking_consent" => "Location Tracking Consent",
            "responder_liability_waiver" => "Responder Liability Waiver",
            "minor_usage_terms" => "Minor Usage Terms",
            "data_sharing_consent" => "Data Sharing Consent",
            "emergency_services_disclosure" => "Emergency Services Disclosure",
            _ => type
        };
    }

    private object[] GetKeyPoints(string type)
    {
        return type switch
        {
            "terms_of_service" => new object[]
            {
                new { title = "Account Responsibility", description = "You are responsible for your account security", is_important = true },
                new { title = "Usage Guidelines", description = "Follow community guidelines when using the platform", is_important = true },
                new { title = "Termination", description = "We may terminate accounts for violations", is_important = false }
            },
            _ => Array.Empty<object>()
        };
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string code, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { code, message });
        return response;
    }

    // DTOs

    private class ConsentSubmissionRequest
    {
        public AgreementConsentDto[] Agreements { get; set; } = Array.Empty<AgreementConsentDto>();
        public string? ConsentMethod { get; set; }
        public string? IpAddress { get; set; }
        public string? DeviceInfo { get; set; }
    }

    private class AgreementConsentDto
    {
        public string AgreementType { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool Accepted { get; set; }
        public bool AcknowledgedKeyPoints { get; set; }
    }
}
