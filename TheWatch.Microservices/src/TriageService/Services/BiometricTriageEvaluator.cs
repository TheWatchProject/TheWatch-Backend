using System.Collections.Concurrent;
using TheWatch.Contracts;

namespace TheWatch.Microservices.Medical.TriageService.Services;

public interface IBiometricTriageEvaluator
{
    Task<BiometricContracts.ManDownStatus> IngestVitalSignsAsync(BiometricContracts.VitalSignsSample sample);
    Task<BiometricContracts.ManDownStatus> ProcessFallAlertAsync(BiometricContracts.FallDetectionAlert fallAlert);
    Task<IEnumerable<BiometricContracts.ManDownStatus>> GetActiveManDownStatusesAsync();
}

public sealed class BiometricTriageEvaluator : IBiometricTriageEvaluator
{
    private readonly ILogger<BiometricTriageEvaluator> _logger;
    private readonly ConcurrentDictionary<string, BiometricContracts.ManDownStatus> _statuses = new();

    public BiometricTriageEvaluator(ILogger<BiometricTriageEvaluator> logger)
    {
        _logger = logger;
    }

    public Task<BiometricContracts.ManDownStatus> IngestVitalSignsAsync(BiometricContracts.VitalSignsSample sample)
    {
        string state = "Normal";
        int countdown = 0;

        // Vitals evaluation: extreme tachycardia (> 160) or bradycardia (< 40), or severe hypoxemia (SpO2 < 85%)
        if (sample.HeartRateBpm > 160 || sample.HeartRateBpm < 40 || (sample.BloodOxygenSpO2.HasValue && sample.BloodOxygenSpO2.Value < 85.0))
        {
            state = "WarningCountdown";
            countdown = 30;
            _logger.LogWarning("Abnormal vitals for responder {ResponderId}: HR {HR} bpm, SpO2 {SpO2}%",
                sample.ResponderId, sample.HeartRateBpm, sample.BloodOxygenSpO2);
        }

        var status = new BiometricContracts.ManDownStatus(
            ResponderId: sample.ResponderId,
            DeviceId: sample.DeviceId,
            StatusState: state,
            RemainingCountdownSeconds: countdown,
            LastMotionDetectedAtUtc: sample.TimestampUtc
        );

        _statuses[sample.ResponderId] = status;
        return Task.FromResult(status);
    }

    public Task<BiometricContracts.ManDownStatus> ProcessFallAlertAsync(BiometricContracts.FallDetectionAlert fallAlert)
    {
        _logger.LogCritical("🚨 CRITICAL FALL IMPACT ({ImpactG}G) detected for responder {ResponderId} at ({Lat}, {Lng})",
            fallAlert.ImpactForceG, fallAlert.ResponderId, fallAlert.Latitude, fallAlert.Longitude);

        var status = new BiometricContracts.ManDownStatus(
            ResponderId: fallAlert.ResponderId,
            DeviceId: fallAlert.DeviceId,
            StatusState: fallAlert.ConfirmedByUser ? "EmergencyTriggered" : "WarningCountdown",
            RemainingCountdownSeconds: fallAlert.ConfirmedByUser ? 0 : 45,
            LastMotionDetectedAtUtc: fallAlert.TriggeredAtUtc
        );

        _statuses[fallAlert.ResponderId] = status;
        return Task.FromResult(status);
    }

    public Task<IEnumerable<BiometricContracts.ManDownStatus>> GetActiveManDownStatusesAsync()
    {
        return Task.FromResult<IEnumerable<BiometricContracts.ManDownStatus>>(_statuses.Values);
    }
}
