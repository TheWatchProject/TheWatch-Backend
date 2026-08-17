using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of step-up authentication service.
/// Manages short-lived tokens for sensitive operations.
/// </summary>
public class StepUpAuthService : IStepUpAuthService
{
    private readonly IJwtService _jwtService;
    private readonly IAuthRepository _authRepository;
    private readonly ICryptographyService _cryptography;
    private readonly ILogger<StepUpAuthService> _logger;

    // In-memory store for challenge tokens (in production, use Redis with TTL)
    private static readonly Dictionary<string, StepUpChallenge> _challenges = new();
    private static readonly object _lock = new();

    public StepUpAuthService(
        IJwtService jwtService,
        IAuthRepository authRepository,
        ICryptographyService cryptography,
        ILogger<StepUpAuthService> logger)
    {
        _jwtService = jwtService;
        _authRepository = authRepository;
        _cryptography = cryptography;
        _logger = logger;
    }

    public Task<string> InitiateStepUpAsync(Guid userId, string purpose, CancellationToken cancellationToken = default)
    {
        // Generate challenge token
        var challengeToken = GenerateSecureToken();

        var challenge = new StepUpChallenge
        {
            ChallengeToken = challengeToken,
            UserId = userId,
            Purpose = purpose,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        lock (_lock)
        {
            _challenges[challengeToken] = challenge;

            // Clean up expired challenges
            var expired = _challenges.Where(kvp => kvp.Value.ExpiresAt < DateTime.UtcNow).ToList();
            foreach (var kvp in expired)
            {
                _challenges.Remove(kvp.Key);
            }
        }

        _logger.LogInformation("Step-up authentication initiated for user {UserId}, purpose: {Purpose}", userId, purpose);

        return Task.FromResult(challengeToken);
    }

    public async Task<string> CompleteStepUpAsync(
        string challengeToken,
        string password,
        string? mfaCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(challengeToken) || string.IsNullOrEmpty(password))
        {
            throw new UnauthorizedAccessException("Challenge token and password are required");
        }

        // Retrieve challenge
        StepUpChallenge? challenge;
        lock (_lock)
        {
            if (!_challenges.TryGetValue(challengeToken, out challenge))
            {
                throw new UnauthorizedAccessException("Invalid or expired challenge token");
            }

            if (challenge.ExpiresAt < DateTime.UtcNow)
            {
                _challenges.Remove(challengeToken);
                throw new UnauthorizedAccessException("Challenge token has expired");
            }

            // Remove challenge (one-time use)
            _challenges.Remove(challengeToken);
        }

        // Verify password
        var user = await _authRepository.GetUserByIdAsync(challenge.UserId);
        if (user == null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        // TODO: Verify password against user's password hash
        // For now, we'll assume password is valid if user exists

        // Check MFA if enrolled
        var mfaEnrollment = await _authRepository.GetActiveMfaEnrollmentAsync(challenge.UserId);
        if (mfaEnrollment != null)
        {
            if (string.IsNullOrEmpty(mfaCode))
            {
                throw new UnauthorizedAccessException("MFA code is required");
            }

            bool mfaValid = false;
            if (mfaEnrollment.Method == "totp")
            {
                var secret = _cryptography.Decrypt(mfaEnrollment.SecretEncrypted!);
                mfaValid = _cryptography.VerifyTotpCode(secret, mfaCode);
            }

            if (!mfaValid)
            {
                throw new UnauthorizedAccessException("Invalid MFA code");
            }
        }

        // Generate step-up token
        var stepUpToken = _jwtService.GenerateStepUpToken(challenge.UserId, challenge.Purpose, 5);

        _logger.LogInformation("Step-up authentication completed for user {UserId}, purpose: {Purpose}", challenge.UserId, challenge.Purpose);

        return stepUpToken;
    }

    public async Task<Guid?> ValidateStepUpTokenAsync(string stepUpToken, string purpose, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stepUpToken))
        {
            return null;
        }

        try
        {
            // Validate JWT token
            var userId = await _jwtService.ValidateAccessTokenAsync(stepUpToken);
            if (userId == null)
            {
                return null;
            }

            // TODO: Extract and verify purpose claim from JWT
            // For now, we assume the token is valid if ValidateAccessTokenAsync succeeds

            return userId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate step-up token");
            return null;
        }
    }

    public string? ExtractStepUpToken(IDictionary<string, IEnumerable<string>> headers)
    {
        if (headers.TryGetValue("X-Step-Up-Token", out var values))
        {
            return values.FirstOrDefault();
        }

        // Also check for lowercase variant
        if (headers.TryGetValue("x-step-up-token", out var lowercaseValues))
        {
            return lowercaseValues.FirstOrDefault();
        }

        return null;
    }

    private string GenerateSecureToken()
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(tokenBytes);
    }

    private class StepUpChallenge
    {
        public string ChallengeToken { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
