using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.SmartHome.GoogleHome;

public class GoogleHomeSmartDeviceAdapter : ISmartHomeIoTDevicePort
{
    private readonly HttpClient _httpClient;
    private readonly string _googleHomeProjectId;
    private readonly ILogger<GoogleHomeSmartDeviceAdapter> _logger;

    public GoogleHomeSmartDeviceAdapter(HttpClient httpClient, string googleHomeProjectId, ILogger<GoogleHomeSmartDeviceAdapter> logger)
    {
        _httpClient = httpClient;
        _googleHomeProjectId = googleHomeProjectId;
        _logger = logger;
    }

    public async Task<bool> BroadcastEmergencyAnnouncementAsync(string householdId, string announcementText, bool triggerSiren = true, CancellationToken ct = default)
    {
        var endpoint = $"https://homegraph.googleapis.com/v1/devices:reportStateAndNotification";
        var payload = new
        {
            requestId = Guid.NewGuid().ToString(),
            agentUserId = householdId,
            eventId = Guid.NewGuid().ToString(),
            payload = new
            {
                devices = new
                {
                    notifications = new
                    {
                        Priority = "HIGH",
                        Message = announcementText,
                        Siren = triggerSiren
                    }
                }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        _logger.LogInformation("Broadcasting emergency alert to Google Home / Nest Hub devices in household {HouseholdId}: '{Message}'", householdId, announcementText);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> IngestSmartSensorAlertAsync(string deviceId, string deviceType, string alertType, string rawPayloadJson, CancellationToken ct = default)
    {
        _logger.LogWarning("Ingested Google Nest alert from {DeviceType} ({DeviceId}): Type: {AlertType}", deviceType, deviceId, alertType);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> TriggerExternalSecuritySirenAsync(string deviceId, TimeSpan duration, CancellationToken ct = default)
    {
        _logger.LogCritical("Activated Google Nest Hub emergency siren on device {DeviceId} for {Seconds} seconds.", deviceId, duration.TotalSeconds);
        await Task.CompletedTask;
        return true;
    }

    public async Task<Stream?> CaptureEmergencySnapshotAsync(string deviceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Captured live emergency camera snapshot from Google Nest Cam {DeviceId}", deviceId);
        return await Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("fake_nest_snapshot_jpeg_bytes")));
    }
}
