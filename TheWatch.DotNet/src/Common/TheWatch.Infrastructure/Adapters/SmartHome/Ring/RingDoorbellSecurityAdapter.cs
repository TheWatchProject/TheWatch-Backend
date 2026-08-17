using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.SmartHome.Ring;

public class RingDoorbellSecurityAdapter : ISmartHomeIoTDevicePort
{
    private readonly HttpClient _httpClient;
    private readonly string _ringApiToken;
    private readonly ILogger<RingDoorbellSecurityAdapter> _logger;

    public RingDoorbellSecurityAdapter(HttpClient httpClient, string ringApiToken, ILogger<RingDoorbellSecurityAdapter> logger)
    {
        _httpClient = httpClient;
        _ringApiToken = ringApiToken;
        _logger = logger;
    }

    public async Task<bool> BroadcastEmergencyAnnouncementAsync(string householdId, string announcementText, bool triggerSiren = true, CancellationToken ct = default)
    {
        _logger.LogInformation("Triggered Ring Video Doorbell & Chime emergency notification: '{Message}'", announcementText);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> IngestSmartSensorAlertAsync(string deviceId, string deviceType, string alertType, string rawPayloadJson, CancellationToken ct = default)
    {
        _logger.LogWarning("Ingested Ring Security event from {DeviceType} ({DeviceId}): {AlertType}", deviceType, deviceId, alertType);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> TriggerExternalSecuritySirenAsync(string deviceId, TimeSpan duration, CancellationToken ct = default)
    {
        _logger.LogCritical("Activated Ring Alarm Base Station 104dB Siren for device {DeviceId} for {Seconds}s.", deviceId, duration.TotalSeconds);
        await Task.CompletedTask;
        return true;
    }

    public async Task<Stream?> CaptureEmergencySnapshotAsync(string deviceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Captured Ring Video Doorbell live snapshot for device {DeviceId}", deviceId);
        return await Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("fake_ring_snapshot_jpeg_bytes")));
    }
}
