using System;
using System.Collections.Concurrent;

namespace TheWatch.Infrastructure.RateLimiting;

/// <summary>
/// High-throughput token bucket rate limiter with priority bypass for emergency SOS distress calls.
/// </summary>
public class EmergencyRateLimiter
{
    private readonly ConcurrentDictionary<string, (int Tokens, DateTime LastRefill)> _buckets = new();
    private const int MaxTokens = 100;
    private const int RefillRatePerSecond = 20;

    /// <summary>
    /// Determines whether an incoming request is allowed.
    /// </summary>
    /// <param name="clientIp">Client IP address.</param>
    /// <param name="isEmergencySos">True if request is a life-safety emergency SOS beacon.</param>
    /// <returns>True if request is permitted; false if rate limit is exceeded.</returns>
    public bool AllowRequest(string clientIp, bool isEmergencySos = false)
    {
        // 1. Life-Safety SOS bypasses rate limits
        if (isEmergencySos) return true;

        var now = DateTime.UtcNow;
        var (tokens, lastRefill) = _buckets.GetOrAdd(clientIp, _ => (MaxTokens, now));

        // Refill tokens based on elapsed time
        var elapsedSeconds = (now - lastRefill).TotalSeconds;
        var newTokens = Math.Min(MaxTokens, tokens + (int)(elapsedSeconds * RefillRatePerSecond));

        if (newTokens > 0)
        {
            _buckets[clientIp] = (newTokens - 1, now);
            return true;
        }

        return false;
    }
}
