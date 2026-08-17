using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Interfaces;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for user profile management and privacy controls.
/// Implements endpoints from user-profile.yaml.
/// </summary>
public class UserProfileFunctions
{
    private readonly ILogger<UserProfileFunctions> _logger;
    private readonly IUserRepository _userRepository;

    public UserProfileFunctions(
        ILogger<UserProfileFunctions> logger,
        IUserRepository userRepository)
    {
        _logger = logger;
        _userRepository = userRepository;
    }

    /// <summary>
    /// GET /users/me - Get current user profile
    /// </summary>
    [Function("GetMyProfile")]
    public async Task<HttpResponseData> GetMyProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/me")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var user = await _userRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "USER_NOT_FOUND", "User not found");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                id = user.UserId,
                first_name = user.PiiState == "anonymized" ? null : user.Name?.Split(' ').FirstOrDefault(),
                last_name = user.PiiState == "anonymized" ? null : user.Name?.Split(' ').LastOrDefault(),
                email = user.PiiState == "anonymized" ? null : user.Email,
                phone = user.PiiState == "anonymized" ? null : user.Phone,
                avatar_url = (string?)null,
                pii_state = user.PiiState,
                roles = DetermineUserRoles(user),
                created_at = user.CreatedAt
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user profile");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving profile");
        }
    }

    /// <summary>
    /// DELETE /users/me - Delete account (anonymize PII)
    /// </summary>
    [Function("DeleteAccount")]
    public async Task<HttpResponseData> DeleteAccount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/me")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var body = await JsonSerializer.DeserializeAsync<DeleteAccountRequest>(req.Body);
            if (body == null || body.ConfirmationPhrase?.ToUpperInvariant() != "DELETE")
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_CONFIRMATION", "You must type DELETE to confirm account deletion");
            }

            var user = await _userRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "USER_NOT_FOUND", "User not found");
            }

            // Schedule anonymization (in production, this would be queued for background processing)
            var scheduledDeletionAt = DateTime.UtcNow.AddDays(7); // Grace period

            // Anonymize user PII immediately (per GDPR requirements, some systems may want a grace period)
            await _userRepository.AnonymizeUserAsync(userId.Value);

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                scheduled_deletion_at = scheduledDeletionAt,
                status = "pending_anonymization"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting account");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during account deletion");
        }
    }

    /// <summary>
    /// GET /users/me/deletion-status - Get account deletion status
    /// </summary>
    [Function("GetDeletionStatus")]
    public async Task<HttpResponseData> GetDeletionStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/me/deletion-status")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var user = await _userRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "USER_NOT_FOUND", "User not found");
            }

            var status = user.PiiState == "anonymized" ? "anonymized" : "not_requested";

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                status,
                scheduled_deletion_at = (DateTime?)null,
                completed_at = user.PiiState == "anonymized" ? user.UpdatedAt : (DateTime?)null
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting deletion status");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving deletion status");
        }
    }

    /// <summary>
    /// POST /users/me/data-export - Request full data export (GDPR)
    /// </summary>
    [Function("RequestDataExport")]
    public async Task<HttpResponseData> RequestDataExport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/me/data-export")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            var user = await _userRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "USER_NOT_FOUND", "User not found");
            }

            // Create export job (in production, this would queue a background job)
            var jobId = Guid.NewGuid();

            _logger.LogInformation($"Data export requested for user {userId}, job ID: {jobId}");

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                job_id = jobId,
                status = "queued",
                created_at = DateTime.UtcNow,
                ready_at = (DateTime?)null,
                download_url = (string?)null,
                expires_at = (DateTime?)null
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting data export");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred requesting data export");
        }
    }

    /// <summary>
    /// GET /users/me/data-export/{jobId} - Get data export status
    /// </summary>
    [Function("GetDataExportStatus")]
    public async Task<HttpResponseData> GetDataExportStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/me/data-export/{jobId}")] HttpRequestData req,
        string jobId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            if (!Guid.TryParse(jobId, out var exportJobId))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_JOB_ID", "Invalid job ID");
            }

            // Note: In production, this would query WatchDbContext for UserDataExportJobs table
            // For now, returning a placeholder response indicating the job is queued
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                job_id = exportJobId,
                status = "queued",
                created_at = DateTime.UtcNow.AddMinutes(-5),
                ready_at = (DateTime?)null,
                download_url = (string?)null,
                expires_at = (DateTime?)null
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting data export status");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving export status");
        }
    }

    // Helper methods

    private string[] DetermineUserRoles(Core.Entities.User user)
    {
        var roles = new List<string> { "user" };
        if (user.AccountType == "responder_eligible")
        {
            roles.Add("responder");
        }
        // TODO: Check for admin role
        return roles.ToArray();
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string code, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { code, message });
        return response;
    }

    // DTOs

    private class DeleteAccountRequest
    {
        public string? Reason { get; set; }
        public string? ConfirmationPhrase { get; set; }
    }
}
