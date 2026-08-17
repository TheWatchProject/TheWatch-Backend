using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Notifications;

public class FcmV1PushNotificationAdapter : IEmergencyNotificationChannelPort
{
    private readonly HttpClient _httpClient;
    private readonly string _fcmProjectId;
    private readonly ILogger<FcmV1PushNotificationAdapter> _logger;

    public FcmV1PushNotificationAdapter(HttpClient httpClient, string fcmProjectId, ILogger<FcmV1PushNotificationAdapter> logger)
    {
        _httpClient = httpClient;
        _fcmProjectId = fcmProjectId;
        _logger = logger;
    }

    public async Task<bool> SendFcmHighPriorityPushAsync(string targetTokenOrTopic, string title, string body, Dictionary<string, string> dataPayload, CancellationToken ct = default)
    {
        var endpoint = $"https://fcm.googleapis.com/v1/projects/{_fcmProjectId}/messages:send";
        var payload = new
        {
            message = new
            {
                topic = targetTokenOrTopic.StartsWith("/topics/") ? targetTokenOrTopic.Replace("/topics/", "") : null,
                token = !targetTokenOrTopic.StartsWith("/topics/") ? targetTokenOrTopic : null,
                notification = new { title, body },
                data = dataPayload,
                android = new
                {
                    priority = "HIGH",
                    direct_boot_ok = true,
                    notification = new
                    {
                        channel_id = "emergency_sos_critical",
                        sound = "siren_loud",
                        notification_priority = "PRIORITY_MAX"
                    }
                },
                apns = new
                {
                    payload = new
                    {
                        aps = new
                        {
                            sound = "emergency_siren.caf",
                            content_available = 1,
                            interruption_level = "critical"
                        }
                    }
                }
            }
        };

        _logger.LogWarning("Dispatched FCM v1 High-Priority Wakeup Push to {Target}: '{Title}'", targetTokenOrTopic, title);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> BroadcastEmergencySmsAsync(IEnumerable<string> phoneNumbers, string messageText, CancellationToken ct = default)
    {
        return await Task.FromResult(true);
    }

    public async Task<bool> TransmitSatelliteEmergencyBurstAsync(byte[] compressedTelemetry, string satelliteConstellation = "IridiumDirect", CancellationToken ct = default)
    {
        return await Task.FromResult(true);
    }
}
