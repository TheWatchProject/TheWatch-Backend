using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for authentication and authorization.
/// Implements endpoints from auth-api.yaml.
/// </summary>
public class AuthFunctions
{
    private readonly ILogger<AuthFunctions> _logger;
    private readonly IAuthRepository _authRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;
    private readonly ICryptographyService _cryptography;

    public AuthFunctions(
        ILogger<AuthFunctions> logger,
        IAuthRepository authRepository,
        IUserRepository userRepository,
        IJwtService jwtService,
        ICryptographyService cryptography)
    {
        _logger = logger;
        _authRepository = authRepository;
        _userRepository = userRepository;
        _jwtService = jwtService;
        _cryptography = cryptography;
    }

    /// <summary>
    /// POST /auth/login - Authenticate user and issue tokens
    /// </summary>
    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<LoginRequest>(req.Body);
            if (body == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Request body is required");
            }

            var identifierHash = _cryptography.ComputeSha256Hash(body.Identifier.ToLowerInvariant());

            // Check rate limiting (5 attempts per 15 minutes)
            var recentAttempts = await _authRepository.GetRecentFailedLoginAttemptsAsync(
                identifierHash,
                TimeSpan.FromMinutes(15));

            if (recentAttempts >= 5)
            {
                return await CreateErrorResponse(req, HttpStatusCode.TooManyRequests, "TOO_MANY_ATTEMPTS", "Too many failed login attempts. Please try again later.");
            }

            // Find user by email or phone
            var user = body.Identifier.Contains("@")
                ? await _authRepository.GetUserByEmailAsync(body.Identifier)
                : await _authRepository.GetUserByPhoneAsync(body.Identifier);

            if (user == null)
            {
                await RecordLoginAttempt(identifierHash, req, false, "user_not_found");
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", "Invalid email/phone or password");
            }

            // Verify password
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                await RecordLoginAttempt(identifierHash, req, false, "no_password_hash");
                _logger.LogWarning("User {UserId} has no password hash set", user.UserId);
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", "Invalid email/phone or password");
            }

            bool passwordValid = _cryptography.VerifyPassword(body.Password, user.PasswordHash);

            if (!passwordValid)
            {
                await RecordLoginAttempt(identifierHash, req, false, "invalid_password");
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", "Invalid email/phone or password");
            }

            await RecordLoginAttempt(identifierHash, req, true, null);

            // Check if MFA is enabled
            var mfaEnrollment = await _authRepository.GetActiveMfaEnrollmentAsync(user.UserId);
            if (mfaEnrollment != null)
            {
                // Generate temporary MFA token
                var mfaToken = _jwtService.GenerateStepUpToken(user.UserId, "mfa_verification", 5);

                var mfaResponse = req.CreateResponse(HttpStatusCode.OK);
                await mfaResponse.WriteAsJsonAsync(new
                {
                    mfa_required = true,
                    mfa_token = mfaToken,
                    mfa_methods = new[] { mfaEnrollment.Method }
                });
                return mfaResponse;
            }

            // Generate tokens
            var roles = DetermineUserRoles(user);
            var accessToken = _jwtService.GenerateAccessToken(user.UserId, roles, 15);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Store refresh token
            var refreshTokenEntity = new RefreshToken
            {
                TokenId = Guid.NewGuid(),
                UserId = user.UserId,
                TokenHash = _cryptography.ComputeSha256Hash(refreshToken),
                DeviceId = body.Device?.DeviceId,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            await _authRepository.CreateRefreshTokenAsync(refreshTokenEntity);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                token_type = "Bearer",
                access_token = accessToken,
                expires_in = 900, // 15 minutes
                refresh_token = refreshToken,
                scope = string.Join(" ", roles)
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during login");
        }
    }

    /// <summary>
    /// POST /auth/refresh - Refresh access token
    /// </summary>
    [Function("RefreshToken")]
    public async Task<HttpResponseData> RefreshToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/refresh")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<RefreshRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.RefreshToken))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Refresh token is required");
            }

            var tokenHash = _cryptography.ComputeSha256Hash(body.RefreshToken);
            var storedToken = await _authRepository.GetRefreshTokenAsync(tokenHash);

            if (storedToken == null || storedToken.RevokedAt != null || storedToken.ExpiresAt < DateTime.UtcNow)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_TOKEN", "Refresh token is invalid or expired");
            }

            var user = await _authRepository.GetUserByIdAsync(storedToken.UserId);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "USER_NOT_FOUND", "User not found");
            }

            // Generate new tokens
            var roles = DetermineUserRoles(user);
            var accessToken = _jwtService.GenerateAccessToken(user.UserId, roles, 15);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            // Revoke old refresh token and create new one
            await _authRepository.RevokeRefreshTokenAsync(storedToken.TokenId, "rotated");

            var newRefreshTokenEntity = new RefreshToken
            {
                TokenId = Guid.NewGuid(),
                UserId = user.UserId,
                TokenHash = _cryptography.ComputeSha256Hash(newRefreshToken),
                DeviceId = body.Device?.DeviceId,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            await _authRepository.CreateRefreshTokenAsync(newRefreshTokenEntity);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                token_type = "Bearer",
                access_token = accessToken,
                expires_in = 900,
                refresh_token = newRefreshToken,
                scope = string.Join(" ", roles)
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during token refresh");
        }
    }

    /// <summary>
    /// POST /auth/logout - Logout and revoke refresh token
    /// </summary>
    [Function("Logout")]
    public async Task<HttpResponseData> Logout(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/logout")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<LogoutRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.RefreshToken))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Refresh token is required");
            }

            var tokenHash = _cryptography.ComputeSha256Hash(body.RefreshToken);
            var storedToken = await _authRepository.GetRefreshTokenAsync(tokenHash);

            if (storedToken != null)
            {
                await _authRepository.RevokeRefreshTokenAsync(storedToken.TokenId, "logout");
            }

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during logout");
        }
    }

    /// <summary>
    /// POST /auth/mfa/enroll - Enroll in MFA
    /// </summary>
    [Function("EnrollMfa")]
    public async Task<HttpResponseData> EnrollMfa(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/mfa/enroll")] HttpRequestData req)
    {
        try
        {
            // TODO: Extract user ID from Bearer token
            var userId = Guid.Empty; // Placeholder

            var body = await JsonSerializer.DeserializeAsync<MfaEnrollRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.Method))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "MFA method is required");
            }

            var enrollment = new MfaEnrollment
            {
                EnrollmentId = Guid.NewGuid(),
                UserId = userId,
                Method = body.Method,
                EnrolledAt = DateTime.UtcNow,
                IsActive = true
            };

            if (body.Method == "totp")
            {
                var secret = _cryptography.GenerateTotpSecret();
                enrollment.SecretEncrypted = _cryptography.Encrypt(secret);

                await _authRepository.CreateMfaEnrollmentAsync(enrollment);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    method = "totp",
                    totp_secret = secret,
                    totp_uri = $"otpauth://totp/TheWatch:{userId}?secret={secret}&issuer=TheWatch",
                    backup_codes = GenerateBackupCodes()
                });
                return response;
            }
            else if (body.Method == "sms")
            {
                if (string.IsNullOrEmpty(body.PhoneNumber))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "PHONE_REQUIRED", "Phone number is required for SMS MFA");
                }

                enrollment.PhoneNumberEncrypted = _cryptography.Encrypt(body.PhoneNumber);
                await _authRepository.CreateMfaEnrollmentAsync(enrollment);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    method = "sms",
                    backup_codes = GenerateBackupCodes()
                });
                return response;
            }

            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_METHOD", "Invalid MFA method");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enrolling in MFA");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during MFA enrollment");
        }
    }

    /// <summary>
    /// POST /auth/mfa/verify - Verify MFA code during login
    /// </summary>
    [Function("VerifyMfa")]
    public async Task<HttpResponseData> VerifyMfa(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/mfa/verify")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<MfaVerifyRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.MfaToken) || string.IsNullOrEmpty(body.Code))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "MFA token and code are required");
            }

            // Validate MFA token and extract user ID
            var userId = await _jwtService.ValidateAccessTokenAsync(body.MfaToken);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_TOKEN", "Invalid MFA token");
            }

            var enrollment = await _authRepository.GetActiveMfaEnrollmentAsync(userId.Value);
            if (enrollment == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "MFA_NOT_ENROLLED", "User is not enrolled in MFA");
            }

            bool codeValid = false;
            if (enrollment.Method == "totp")
            {
                var secret = _cryptography.Decrypt(enrollment.SecretEncrypted!);
                codeValid = _cryptography.VerifyTotpCode(secret, body.Code);
            }
            else if (enrollment.Method == "sms")
            {
                // TODO: Implement SMS code verification (stored in separate table with expiration)
                codeValid = true; // Placeholder
            }

            if (!codeValid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_CODE", "Invalid MFA code");
            }

            var user = await _authRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "USER_NOT_FOUND", "User not found");
            }

            // Generate final tokens
            var roles = DetermineUserRoles(user);
            var accessToken = _jwtService.GenerateAccessToken(user.UserId, roles, 15);
            var refreshToken = _jwtService.GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken
            {
                TokenId = Guid.NewGuid(),
                UserId = user.UserId,
                TokenHash = _cryptography.ComputeSha256Hash(refreshToken),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            await _authRepository.CreateRefreshTokenAsync(refreshTokenEntity);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                token_type = "Bearer",
                access_token = accessToken,
                expires_in = 900,
                refresh_token = refreshToken,
                scope = string.Join(" ", roles)
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying MFA");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during MFA verification");
        }
    }

    /// <summary>
    /// GET /.well-known/jwks.json - Get JSON Web Key Set
    /// </summary>
    [Function("GetJwks")]
    public async Task<HttpResponseData> GetJwks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/jwks.json")] HttpRequestData req)
    {
        try
        {
            var jwks = await _jwtService.GetJwksJsonAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(jwks);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting JWKS");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving JWKS");
        }
    }

    /// <summary>
    /// GET /auth/me - Get current identity (whoami)
    /// </summary>
    [Function("WhoAmI")]
    public async Task<HttpResponseData> WhoAmI(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/me")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var user = await _authRepository.GetUserByIdAsync(userId.Value);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, "USER_NOT_FOUND", "User not found");
            }

            var roles = DetermineUserRoles(user);
            var permissions = DetermineUserPermissions(roles);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                user_id = user.UserId,
                roles,
                permissions
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user identity");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred retrieving identity");
        }
    }

    /// <summary>
    /// POST /auth/step-up/challenge - Create step-up challenge
    /// </summary>
    [Function("CreateStepUpChallenge")]
    public async Task<HttpResponseData> CreateStepUpChallenge(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/step-up/challenge")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var body = await JsonSerializer.DeserializeAsync<StepUpChallengeRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.Purpose))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Purpose is required");
            }

            var challengeId = Guid.NewGuid();
            var expiresAt = DateTime.UtcNow.AddMinutes(5);

            // TODO: Store challenge in cache/database with short TTL

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                challenge_id = challengeId,
                expires_at = expiresAt,
                method_hint = "biometric"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating step-up challenge");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred creating step-up challenge");
        }
    }

    /// <summary>
    /// POST /auth/step-up/verify - Verify step-up completion
    /// </summary>
    [Function("VerifyStepUp")]
    public async Task<HttpResponseData> VerifyStepUp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/step-up/verify")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var body = await JsonSerializer.DeserializeAsync<StepUpVerifyRequest>(req.Body);
            if (body == null || body.ChallengeId == Guid.Empty)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Challenge ID is required");
            }

            // TODO: Validate challenge exists and not expired
            // TODO: Verify proof based on method (biometric/pin)

            var stepUpToken = _jwtService.GenerateStepUpToken(userId.Value, "step_up_verified", 5);
            var expiresAt = DateTime.UtcNow.AddMinutes(5);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                step_up_token = stepUpToken,
                expires_at = expiresAt
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying step-up");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred verifying step-up");
        }
    }

    /// <summary>
    /// POST /auth/password-reset/request - Request password reset
    /// </summary>
    [Function("RequestPasswordReset")]
    public async Task<HttpResponseData> RequestPasswordReset(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/password-reset/request")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<PasswordResetRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.Identifier))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Identifier is required");
            }

            // Always return 202 to prevent user enumeration
            var response = req.CreateResponse(HttpStatusCode.Accepted);

            // Find user
            var user = body.Identifier.Contains("@")
                ? await _authRepository.GetUserByEmailAsync(body.Identifier)
                : await _authRepository.GetUserByPhoneAsync(body.Identifier);

            if (user != null)
            {
                // Generate reset code
                var resetCode = _cryptography.GenerateVerificationCode();
                var resetToken = new PasswordResetToken
                {
                    TokenId = Guid.NewGuid(),
                    UserId = user.UserId,
                    TokenHash = _cryptography.ComputeSha256Hash(resetCode),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };

                await _authRepository.CreatePasswordResetTokenAsync(resetToken);

                // TODO: Send reset code via email or SMS based on delivery_method
                _logger.LogInformation("Password reset requested for user (not logging identifier for PII protection)");
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting password reset");
            return req.CreateResponse(HttpStatusCode.Accepted); // Still return 202 on error
        }
    }

    /// <summary>
    /// POST /auth/password-reset/verify - Verify reset code and set new password
    /// </summary>
    [Function("VerifyPasswordReset")]
    public async Task<HttpResponseData> VerifyPasswordReset(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/password-reset/verify")] HttpRequestData req)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<PasswordResetVerifyRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.Identifier) || string.IsNullOrEmpty(body.Code) || string.IsNullOrEmpty(body.NewPassword))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "Identifier, code, and new password are required");
            }

            var codeHash = _cryptography.ComputeSha256Hash(body.Code);
            var resetToken = await _authRepository.GetPasswordResetTokenAsync(codeHash);

            if (resetToken == null || resetToken.UsedAt != null || resetToken.ExpiresAt < DateTime.UtcNow)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_CODE", "Invalid or expired reset code");
            }

            var user = await _authRepository.GetUserByIdAsync(resetToken.UserId);
            if (user == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "USER_NOT_FOUND", "User not found");
            }

            // Update password
            var passwordHash = _cryptography.HashPassword(body.NewPassword);
            await _authRepository.UpdateUserPasswordHashAsync(user.UserId, passwordHash);

            // Mark reset token as used
            await _authRepository.MarkPasswordResetTokenUsedAsync(resetToken.TokenId);

            // Generate new access and refresh tokens
            var roles = DetermineUserRoles(user);
            var accessToken = _jwtService.GenerateAccessToken(user.UserId, roles, 15);
            var refreshToken = _jwtService.GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken
            {
                TokenId = Guid.NewGuid(),
                UserId = user.UserId,
                TokenHash = _cryptography.ComputeSha256Hash(refreshToken),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            await _authRepository.CreateRefreshTokenAsync(refreshTokenEntity);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                token_type = "Bearer",
                access_token = accessToken,
                expires_in = 900,
                refresh_token = refreshToken,
                scope = string.Join(" ", roles)
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying password reset");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred during password reset");
        }
    }

    /// <summary>
    /// GET /auth/sessions - List active sessions
    /// </summary>
    [Function("ListSessions")]
    public async Task<HttpResponseData> ListSessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/sessions")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var sessions = await _authRepository.GetActiveSessionsForUserAsync(userId.Value);

            var sessionDtos = sessions.Select(s => new
            {
                session_id = s.SessionId,
                device = new
                {
                    device_id = s.DeviceId
                },
                created_at = s.CreatedAt,
                last_active_at = s.LastActivityAt,
                ip_address = s.IpAddress,
                is_current = false // TODO: Determine if this is the current session
            }).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(sessionDtos);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing sessions");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred listing sessions");
        }
    }

    /// <summary>
    /// DELETE /auth/sessions/{sessionId} - Revoke a specific session
    /// </summary>
    [Function("RevokeSession")]
    public async Task<HttpResponseData> RevokeSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "auth/sessions/{sessionId}")] HttpRequestData req,
        string sessionId)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            if (!Guid.TryParse(sessionId, out var sessionGuid))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_SESSION_ID", "Invalid session ID");
            }

            // TODO: Verify session belongs to user before revoking
            await _authRepository.RevokeSessionTokenAsync(sessionGuid);

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking session");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred revoking session");
        }
    }

    /// <summary>
    /// POST /auth/mfa/disable - Disable MFA
    /// </summary>
    [Function("DisableMfa")]
    public async Task<HttpResponseData> DisableMfa(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/mfa/disable")] HttpRequestData req)
    {
        try
        {
            var userId = await ExtractUserIdFromToken(req);
            if (userId == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Valid access token required");
            }

            var body = await JsonSerializer.DeserializeAsync<MfaDisableRequest>(req.Body);
            if (body == null || string.IsNullOrEmpty(body.Code))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "INVALID_REQUEST", "MFA code is required");
            }

            var enrollment = await _authRepository.GetActiveMfaEnrollmentAsync(userId.Value);
            if (enrollment == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "MFA_NOT_ENROLLED", "MFA is not enabled");
            }

            // Verify code before disabling
            bool codeValid = false;
            if (enrollment.Method == "totp")
            {
                var secret = _cryptography.Decrypt(enrollment.SecretEncrypted!);
                codeValid = _cryptography.VerifyTotpCode(secret, body.Code);
            }

            if (!codeValid)
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "INVALID_CODE", "Invalid MFA code");
            }

            await _authRepository.DeactivateMfaEnrollmentAsync(enrollment.EnrollmentId);

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling MFA");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An error occurred disabling MFA");
        }
    }

    // Helper methods

    private async Task RecordLoginAttempt(string identifierHash, HttpRequestData req, bool success, string? failureReason)
    {
        var attempt = new LoginAttempt
        {
            AttemptId = Guid.NewGuid(),
            IdentifierHash = identifierHash,
            IpAddress = req.Headers.TryGetValues("X-Forwarded-For", out var values)
                ? values.First()
                : "unknown",
            UserAgent = req.Headers.TryGetValues("User-Agent", out var ua)
                ? ua.First()
                : null,
            Success = success,
            FailureReason = failureReason,
            AttemptedAt = DateTime.UtcNow
        };

        await _authRepository.RecordLoginAttemptAsync(attempt);
    }

    private string[] DetermineUserRoles(User user)
    {
        var roles = new List<string> { "summoner" };

        if (user.AccountType == "responder_eligible")
        {
            roles.Add("responder");
        }

        // TODO: Check for HQ/admin roles in a separate table or user property

        return roles.ToArray();
    }

    private string[] DetermineUserPermissions(string[] roles)
    {
        var permissions = new List<string>();

        foreach (var role in roles)
        {
            switch (role)
            {
                case "summoner":
                    permissions.AddRange(new[] {
                        "summoner:create_incident",
                        "summoner:view_own_history",
                        "summoner:manage_triggers"
                    });
                    break;
                case "responder":
                    permissions.AddRange(new[] {
                        "responder:accept_dispatch",
                        "responder:submit_evidence",
                        "responder:trigger_distress"
                    });
                    break;
                case "hq":
                    permissions.AddRange(new[] {
                        "hq:send_broadcast",
                        "hq:view_all_incidents",
                        "hq:place_legal_hold"
                    });
                    break;
                case "admin":
                    permissions.AddRange(new[] {
                        "admin:manage_users",
                        "admin:manage_agreements",
                        "admin:view_audit_logs"
                    });
                    break;
            }
        }

        return permissions.Distinct().ToArray();
    }

    private async Task<Guid?> ExtractUserIdFromToken(HttpRequestData req)
    {
        try
        {
            if (!req.Headers.TryGetValues("Authorization", out var authHeaders))
            {
                return null;
            }

            var authHeader = authHeaders.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return null;
            }

            var token = authHeader.Substring("Bearer ".Length);
            return await _jwtService.ValidateAccessTokenAsync(token);
        }
        catch
        {
            return null;
        }
    }

    private string[] GenerateBackupCodes()
    {
        var codes = new string[10];
        for (int i = 0; i < 10; i++)
        {
            codes[i] = _cryptography.GenerateVerificationCode() + _cryptography.GenerateVerificationCode();
        }
        return codes;
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string code, string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new
        {
            code,
            message
        });
        return response;
    }

    // Request/Response DTOs

    private class LoginRequest
    {
        public string Identifier { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public DeviceContext? Device { get; set; }
    }

    private class RefreshRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
        public DeviceContext? Device { get; set; }
    }

    private class LogoutRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    private class MfaEnrollRequest
    {
        public string Method { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
    }

    private class MfaVerifyRequest
    {
        public string MfaToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    private class DeviceContext
    {
        public string? DeviceId { get; set; }
        public string? Platform { get; set; }
        public string? AppVersion { get; set; }
    }

    private class StepUpChallengeRequest
    {
        public string Purpose { get; set; } = string.Empty;
        public Guid? IncidentId { get; set; }
    }

    private class StepUpVerifyRequest
    {
        public Guid ChallengeId { get; set; }
        public StepUpProof? Proof { get; set; }
    }

    private class StepUpProof
    {
        public string Method { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
    }

    private class PasswordResetRequest
    {
        public string Identifier { get; set; } = string.Empty;
        public string DeliveryMethod { get; set; } = "email";
    }

    private class PasswordResetVerifyRequest
    {
        public string Identifier { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    private class MfaDisableRequest
    {
        public string Code { get; set; } = string.Empty;
    }
}
