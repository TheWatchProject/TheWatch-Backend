using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for safety settings management.
/// Implements endpoints from safety-settings-api.yaml
/// </summary>
public class SafetySettingsFunctions
{
    private readonly ILogger<SafetySettingsFunctions> _logger;
    private readonly ISafetySettingsRepository _safetySettingsRepository;
    private readonly IDuressPinService _duressPinService;
    private readonly IUserRepository _userRepository;
    private readonly ICryptographyService _cryptographyService;

    public SafetySettingsFunctions(
        ILogger<SafetySettingsFunctions> logger,
        ISafetySettingsRepository safetySettingsRepository,
        IDuressPinService duressPinService,
        IUserRepository userRepository,
        ICryptographyService cryptographyService)
    {
        _logger = logger;
        _safetySettingsRepository = safetySettingsRepository;
        _duressPinService = duressPinService;
        _userRepository = userRepository;
        _cryptographyService = cryptographyService;
    }

    /// <summary>
    /// GET /users/me/safety-profile - Get safety configuration
    /// </summary>
    [Function("GetSafetyProfile")]
    public async Task<HttpResponseData> GetSafetyProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/me/safety-profile")] HttpRequestData req)
    {
        _logger.LogInformation("Getting safety profile");

        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var settings = await _safetySettingsRepository.GetByUserIdAsync(userId.Value);
            if (settings == null)
            {
                // Create default settings if none exist
                settings = new SafetySettings
                {
                    UserId = userId.Value,
                    VoiceActivationEnabled = true,
                    StealthModeEnabled = false,
                    FalsePositiveProtection = "medium",
                    AlertRadiusMeters = 500,
                    IncludeDesignatedResponders = true,
                    TrustedContactsOnly = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                settings = await _safetySettingsRepository.CreateOrUpdateAsync(settings);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                user_id = settings.UserId,
                voice_activation_enabled = settings.VoiceActivationEnabled,
                stealth_mode_enabled = settings.StealthModeEnabled,
                false_positive_protection = settings.FalsePositiveProtection,
                community_settings = new
                {
                    alert_radius_meters = settings.AlertRadiusMeters,
                    include_designated_responders = settings.IncludeDesignatedResponders,
                    trusted_contacts_only = settings.TrustedContactsOnly
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting safety profile");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while retrieving safety profile" });
            return errorResponse;
        }
    }

    /// <summary>
    /// PATCH /users/me/safety-profile - Update global safety preferences
    /// </summary>
    [Function("UpdateSafetyProfile")]
    public async Task<HttpResponseData> UpdateSafetyProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "users/me/safety-profile")] HttpRequestData req)
    {
        _logger.LogInformation("Updating safety profile");

        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var requestBody = await req.ReadFromJsonAsync<SafetyProfileUpdateRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            var settings = await _safetySettingsRepository.GetByUserIdAsync(userId.Value);
            if (settings == null)
            {
                settings = new SafetySettings
                {
                    UserId = userId.Value,
                    CreatedAt = DateTime.UtcNow
                };
            }

            // Update fields if provided
            if (requestBody.VoiceActivationEnabled.HasValue)
                settings.VoiceActivationEnabled = requestBody.VoiceActivationEnabled.Value;

            if (requestBody.StealthModeEnabled.HasValue)
                settings.StealthModeEnabled = requestBody.StealthModeEnabled.Value;

            if (requestBody.FalsePositiveProtection != null)
            {
                if (requestBody.FalsePositiveProtection != "low" &&
                    requestBody.FalsePositiveProtection != "medium" &&
                    requestBody.FalsePositiveProtection != "high")
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { code = "INVALID_VALUE", message = "false_positive_protection must be low, medium, or high" });
                    return badRequest;
                }
                settings.FalsePositiveProtection = requestBody.FalsePositiveProtection;
            }

            settings.UpdatedAt = DateTime.UtcNow;

            var updated = await _safetySettingsRepository.CreateOrUpdateAsync(settings);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                user_id = updated.UserId,
                voice_activation_enabled = updated.VoiceActivationEnabled,
                stealth_mode_enabled = updated.StealthModeEnabled,
                false_positive_protection = updated.FalsePositiveProtection,
                community_settings = new
                {
                    alert_radius_meters = updated.AlertRadiusMeters,
                    include_designated_responders = updated.IncludeDesignatedResponders,
                    trusted_contacts_only = updated.TrustedContactsOnly
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating safety profile");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while updating safety profile" });
            return errorResponse;
        }
    }

    /// <summary>
    /// PUT /users/me/community-settings - Configure community response scope
    /// </summary>
    [Function("UpdateCommunitySettings")]
    public async Task<HttpResponseData> UpdateCommunitySettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/me/community-settings")] HttpRequestData req)
    {
        _logger.LogInformation("Updating community settings");

        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var requestBody = await req.ReadFromJsonAsync<CommunitySettingsRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            var settings = await _safetySettingsRepository.GetByUserIdAsync(userId.Value);
            if (settings == null)
            {
                settings = new SafetySettings
                {
                    UserId = userId.Value,
                    CreatedAt = DateTime.UtcNow
                };
            }

            // Validate alert radius
            if (requestBody.AlertRadiusMeters < 100 || requestBody.AlertRadiusMeters > 2000)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_RADIUS", message = "alert_radius_meters must be between 100 and 2000" });
                return badRequest;
            }

            settings.AlertRadiusMeters = requestBody.AlertRadiusMeters;
            settings.IncludeDesignatedResponders = requestBody.IncludeDesignatedResponders;
            settings.TrustedContactsOnly = requestBody.TrustedContactsOnly;
            settings.UpdatedAt = DateTime.UtcNow;

            var updated = await _safetySettingsRepository.CreateOrUpdateAsync(settings);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                alert_radius_meters = updated.AlertRadiusMeters,
                include_designated_responders = updated.IncludeDesignatedResponders,
                trusted_contacts_only = updated.TrustedContactsOnly
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating community settings");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while updating community settings" });
            return errorResponse;
        }
    }

    /// <summary>
    /// PUT /users/me/duress-pin - Set or update duress PIN
    /// CRITICAL: Duress PIN enables silent escalation during forced cancellations
    /// </summary>
    [Function("SetDuressPin")]
    public async Task<HttpResponseData> SetDuressPin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/me/duress-pin")] HttpRequestData req)
    {
        _logger.LogInformation("Setting duress PIN");

        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var requestBody = await req.ReadFromJsonAsync<DuressPinRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            // Validate PIN format (numeric, 4-8 digits)
            if (string.IsNullOrWhiteSpace(requestBody.DuressPin) ||
                requestBody.DuressPin.Length < 4 ||
                requestBody.DuressPin.Length > 8 ||
                !requestBody.DuressPin.All(char.IsDigit))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_PIN", message = "duress_pin must be 4-8 numeric digits" });
                return badRequest;
            }

            // Verify current password before allowing PIN change
            var user = await _userRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { code = "USER_NOT_FOUND", message = "User not found" });
                return notFound;
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
                !_cryptographyService.VerifyPassword(requestBody.CurrentPassword, user.PasswordHash))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "INVALID_PASSWORD", message = "Current password is incorrect" });
                return unauthorized;
            }

            // Use DuressPinService to set the duress PIN
            await _duressPinService.SetDuressPinAsync(userId.Value, requestBody.DuressPin);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Duress PIN set successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting duress PIN");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while setting duress PIN" });
            return errorResponse;
        }
    }

    /// <summary>
    /// PUT /users/me/safe-pin - Set or update safe cancellation PIN
    /// </summary>
    [Function("SetSafePin")]
    public async Task<HttpResponseData> SetSafePin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/me/safe-pin")] HttpRequestData req)
    {
        _logger.LogInformation("Setting safe PIN");

        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var requestBody = await req.ReadFromJsonAsync<SafePinRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            // Validate PIN format (numeric, 4-8 digits)
            if (string.IsNullOrWhiteSpace(requestBody.SafePin) ||
                requestBody.SafePin.Length < 4 ||
                requestBody.SafePin.Length > 8 ||
                !requestBody.SafePin.All(char.IsDigit))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_PIN", message = "safe_pin must be 4-8 numeric digits" });
                return badRequest;
            }

            // Verify current password before allowing PIN change
            var user = await _userRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { code = "USER_NOT_FOUND", message = "User not found" });
                return notFound;
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
                !_cryptographyService.VerifyPassword(requestBody.CurrentPassword, user.PasswordHash))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "INVALID_PASSWORD", message = "Current password is incorrect" });
                return unauthorized;
            }

            // Use DuressPinService to set the safe PIN
            await _duressPinService.SetSafePinAsync(userId.Value, requestBody.SafePin);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Safe PIN set successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting safe PIN");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while setting safe PIN" });
            return errorResponse;
        }
    }

    /// <summary>
    /// DELETE /users/me/duress-pin - Remove duress PIN
    /// </summary>
    [Function("RemoveDuressPin")]
    public async Task<HttpResponseData> RemoveDuressPin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/me/duress-pin")] HttpRequestData req)
    {
        _logger.LogInformation("Removing duress PIN");

        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var requestBody = await req.ReadFromJsonAsync<RemovePinRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { code = "INVALID_REQUEST", message = "Invalid request body" });
                return badRequest;
            }

            // Verify current password before allowing PIN removal
            var user = await _userRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { code = "USER_NOT_FOUND", message = "User not found" });
                return notFound;
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
                !_cryptographyService.VerifyPassword(requestBody.CurrentPassword, user.PasswordHash))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "INVALID_PASSWORD", message = "Current password is incorrect" });
                return unauthorized;
            }

            // Remove duress PIN by calling service with null/empty
            await _duressPinService.RemoveDuressPinAsync(userId.Value);

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing duress PIN");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "An error occurred while removing duress PIN" });
            return errorResponse;
        }
    }

    // Request DTOs
    private class SafetyProfileUpdateRequest
    {
        public bool? VoiceActivationEnabled { get; set; }
        public bool? StealthModeEnabled { get; set; }
        public string? FalsePositiveProtection { get; set; }
    }

    private class CommunitySettingsRequest
    {
        public int AlertRadiusMeters { get; set; } = 500;
        public bool IncludeDesignatedResponders { get; set; } = true;
        public bool TrustedContactsOnly { get; set; } = false;
    }

    private class DuressPinRequest
    {
        public string DuressPin { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
    }

    private class SafePinRequest
    {
        public string SafePin { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
    }

    private class RemovePinRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
    }
}
