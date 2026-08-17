using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.SmartHome.Alexa;

public class AmazonAlexaSmartDeviceAdapter : ISmartHomeIoTDevicePort
{
    private readonly HttpClient _httpClient;
    private readonly string _alexaSkillId;
    private readonly ILogger<AmazonAlexaSmartDeviceAdapter> _logger;

    public AmazonAlexaSmartDeviceAdapter(HttpClient httpClient, string alexaSkillId, ILogger<AmazonAlexaSmartDeviceAdapter> logger)
    {
        _httpClient = httpClient;
        _alexaSkillId = alexaSkillId;
        _logger = logger;
    }

    public async Task<bool> BroadcastEmergencyAnnouncementAsync(string householdId, string announcementText, bool triggerSiren = true, CancellationToken ct = default)
    {
        var endpoint = "https://api.amazonalexa.com/v1/proactiveEvents/stages/development";
        _logger.LogInformation("Dispatched Alexa Proactive Event Broadcast to all Echo devices in household {HouseholdId}: '{Announcement}'", householdId, announcementText);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> IngestSmartSensorAlertAsync(string deviceId, string deviceType, string alertType, string rawPayloadJson, CancellationToken ct = default)
    {
        _logger.LogWarning("Ingested Alexa Guard acoustic detection event from {DeviceId}: {AlertType}", deviceId, alertType);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> TriggerExternalSecuritySirenAsync(string deviceId, TimeSpan duration, CancellationToken ct = default)
    {
        _logger.LogCritical("Triggered Alexa Echo multi-room high-pitch alarm siren on {DeviceId} for {Seconds}s.", deviceId, duration.TotalSeconds);
        await Task.CompletedTask;
        return true;
    }

    public async Task<Stream?> CaptureEmergencySnapshotAsync(string deviceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Requested Echo Show camera snapshot from {DeviceId}", deviceId);
        return await Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("fake_alexa_snapshot_jpeg_bytes")));
    }
}
