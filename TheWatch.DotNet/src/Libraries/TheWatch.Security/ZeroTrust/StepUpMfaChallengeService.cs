using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Security.ZeroTrust;

public class StepUpMfaChallengeService
{
    private readonly ConcurrentDictionary<string, (string ChallengeToken, DateTimeOffset ExpiresAt)> _activeChallenges = new();
    private readonly ILogger<StepUpMfaChallengeService> _logger;

    public StepUpMfaChallengeService(ILogger<StepUpMfaChallengeService> logger)
    {
        _logger = logger;
    }

    public Task<string> IssueChallengeAsync(string userId, string sensitiveAction, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _activeChallenges[userId] = (token, DateTimeOffset.UtcNow.AddMinutes(5));
        _logger.LogWarning("Step-Up FIDO2 challenge issued for user {UserId} executing {Action}", userId, sensitiveAction);
        return Task.FromResult(token);
    }

    public Task<bool> VerifyChallengeResponseAsync(string userId, string challengeResponseToken, CancellationToken ct = default)
    {
        if (_activeChallenges.TryRemove(userId, out var challenge) && challenge.ExpiresAt > DateTimeOffset.UtcNow)
        {
            var valid = challenge.ChallengeToken == challengeResponseToken;
            _logger.LogInformation("Step-Up challenge verification result for {UserId}: {Valid}", userId, valid);
            return Task.FromResult(valid);
        }
        return Task.FromResult(false);
    }
}
