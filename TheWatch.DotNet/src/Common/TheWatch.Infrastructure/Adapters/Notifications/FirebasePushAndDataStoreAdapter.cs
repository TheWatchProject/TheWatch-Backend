using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Notifications;

public interface IFirebasePort : INotificationPort
{
    Task<bool> PushEmergencyBroadcastAsync(string topic, string title, string alertBody, Dictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task<bool> SyncRealtimeRecordAsync(string path, object payload, CancellationToken ct = default);
}

public class FirebasePushAndDataStoreAdapter : IFirebasePort
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FirebasePushAndDataStoreAdapter> _logger;
    private readonly string _projectId;
    private readonly string _databaseUrl;

    public FirebasePushAndDataStoreAdapter(HttpClient httpClient, string projectId, string databaseUrl, ILogger<FirebasePushAndDataStoreAdapter> logger)
    {
        _httpClient = httpClient;
        _projectId = projectId;
        _databaseUrl = databaseUrl.TrimEnd('/');
        _logger = logger;
    }

    public async Task<bool> SendPushNotificationAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var fcmPayload = new
        {
            message = new
            {
                token = message.TargetToken,
                notification = new
                {
                    title = message.Title,
                    body = message.Body
                },
                data = message.Data ?? new Dictionary<string, string>()
            }
        };

        return await SendFcmRequestAsync(fcmPayload, ct);
    }

    public async Task<bool> SendSmsAlertAsync(string phoneNumber, string messageText, CancellationToken ct = default)
    {
        _logger.LogInformation("FCM dispatch SMS fallback bridge requested for recipient hash: {RecipientHash}", phoneNumber.GetHashCode());
        var data = new Dictionary<string, string>
        {
            { "channel", "sms_fallback" },
            { "phone_hash", phoneNumber.GetHashCode().ToString() }
        };
        return await SendPushNotificationAsync(new NotificationMessage("Emergency SMS Dispatch", messageText, "sms_gateway_token", data), ct);
    }

    public async Task<bool> PushEmergencyBroadcastAsync(string topic, string title, string alertBody, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var fcmPayload = new
        {
            message = new
            {
                topic = topic,
                notification = new
                {
                    title = title,
                    body = alertBody
                },
                data = metadata ?? new Dictionary<string, string>(),
                android = new
                {
                    priority = "high",
                    notification = new
                    {
                        channel_id = "emergency_sos",
                        sound = "emergency_alarm.wav"
                    }
                },
                apns = new
                {
                    payload = new
                    {
                        aps = new
                        {
                            sound = "emergency_alarm.caf",
                            content_available = 1
                        }
                    }
                }
            }
        };

        return await SendFcmRequestAsync(fcmPayload, ct);
    }

    public async Task<bool> SyncRealtimeRecordAsync(string path, object payload, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_databaseUrl}/{path.TrimStart('/')}.json";
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(url, content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync realtime document to Firebase at {Path}", path);
            return false;
        }
    }

    private async Task<bool> SendFcmRequestAsync(object fcmPayload, CancellationToken ct)
    {
        try
        {
            var endpoint = $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send";
            var json = JsonSerializer.Serialize(fcmPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully dispatched FCM message");
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("FCM dispatch failed with code {StatusCode}: {Error}", response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception encountered during Firebase push dispatch");
            return false;
        }
    }
}
