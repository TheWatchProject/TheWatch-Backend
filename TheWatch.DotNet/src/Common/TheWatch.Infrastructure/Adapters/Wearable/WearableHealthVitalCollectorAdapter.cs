using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Adapters.Wearable;

public enum WearableDevicePlatform
{
    AppleWatch = 1,
    SamsungGalaxyWatch = 2,
    GarminTactical = 3,
    WearOSGeneric = 4
}

public sealed record WearableVitalSample(
    string DeviceId,
    string UserId,
    WearableDevicePlatform Platform,
    double HeartRateBpm,
    double BloodOxygenPercentage,
    double BodyTemperatureCelsius,
    int RespiratoryRateBreathsPerMinute,
    bool FallDetected,
    bool EcgArrhythmiaDetected,
    double StressScore,
    DateTime RecordedAtUtc
);

public interface IWearableHealthVitalCollectorAdapter
{
    Task RecordVitalsAsync(WearableVitalSample sample);
    IReadOnlyList<WearableVitalSample> GetUserVitalsHistory(string userId, TimeSpan lookback);
    bool IsUserInAcuteDistress(string userId, out string distressReason);
}

/// <summary>
/// Multi-Platform Wearable Vital Ingestion Adapter (Apple Watch HealthKit, Samsung Health, Garmin).
/// </summary>
public sealed class WearableHealthVitalCollectorAdapter : IWearableHealthVitalCollectorAdapter
{
    private readonly ILogger<WearableHealthVitalCollectorAdapter> _logger;
    private readonly ConcurrentDictionary<string, List<WearableVitalSample>> _vitalsByUser = new();

    public WearableHealthVitalCollectorAdapter(ILogger<WearableHealthVitalCollectorAdapter> logger)
    {
        _logger = logger;
    }

    public Task RecordVitalsAsync(WearableVitalSample sample)
    {
        _vitalsByUser.AddOrUpdate(
            sample.UserId,
            new List<WearableVitalSample> { sample },
            (_, list) => { lock (list) { list.Add(sample); return list; } }
        );

        if (sample.FallDetected || sample.HeartRateBpm > 180 || (sample.BloodOxygenPercentage > 0 && sample.BloodOxygenPercentage < 88))
        {
            _logger.LogWarning("ACUTE WEARABLE DISTRESS DETECTED for user {UserId}: HR={HR}, SpO2={SpO2}%, Fall={Fall}",
                sample.UserId, sample.HeartRateBpm, sample.BloodOxygenPercentage, sample.FallDetected);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<WearableVitalSample> GetUserVitalsHistory(string userId, TimeSpan lookback)
    {
        if (!_vitalsByUser.TryGetValue(userId, out var list)) return Array.Empty<WearableVitalSample>();
        var cutoff = DateTime.UtcNow - lookback;

        lock (list)
        {
            return list.Where(s => s.RecordedAtUtc >= cutoff).OrderByDescending(s => s.RecordedAtUtc).ToList();
        }
    }

    public bool IsUserInAcuteDistress(string userId, out string distressReason)
    {
        distressReason = string.Empty;
        var recent = GetUserVitalsHistory(userId, TimeSpan.FromMinutes(5));
        if (!recent.Any()) return false;

        var latest = recent.First();
        if (latest.FallDetected)
        {
            distressReason = $"Confirmed hardware fall detection from {latest.Platform}";
            return true;
        }

        if (latest.HeartRateBpm >= 190)
        {
            distressReason = $"Severe tachycardia: {latest.HeartRateBpm:F0} BPM";
            return true;
        }

        if (latest.BloodOxygenPercentage is > 0 and < 85)
        {
            distressReason = $"Critical hypoxemia: SpO2 {latest.BloodOxygenPercentage:F0}%";
            return true;
        }

        if (latest.EcgArrhythmiaDetected)
        {
            distressReason = $"Acute ECG arrhythmia event detected";
            return true;
        }

        return false;
    }
}
