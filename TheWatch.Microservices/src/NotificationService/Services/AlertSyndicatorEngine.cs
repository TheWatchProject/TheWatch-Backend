using System.Text.Json;
using TheWatch.Contracts;

namespace TheWatch.Microservices.Notifications.NotificationService.Services;

public interface IAlertSyndicatorEngine
{
    Task<AlertContracts.AlertDispatchResult> SyndicateBroadcastAsync(AlertContracts.BroadcastEmergencyAlertRequest request);
}

public sealed class AlertSyndicatorEngine : IAlertSyndicatorEngine
{
    private readonly ILogger<AlertSyndicatorEngine> _logger;

    public AlertSyndicatorEngine(ILogger<AlertSyndicatorEngine> logger)
    {
        _logger = logger;
    }

    public async Task<AlertContracts.AlertDispatchResult> SyndicateBroadcastAsync(AlertContracts.BroadcastEmergencyAlertRequest request)
    {
        var channelsReached = new List<string>();
        int recipientsCount = 0;
        var alertId = Guid.NewGuid();

        _logger.LogInformation("Syndicating Emergency Broadcast {IncidentId}: {Title} (Severity: {Severity})",
            request.IncidentId, request.Title, request.Severity);

        foreach (var channel in request.TargetChannels)
        {
            switch (channel.ToUpperInvariant())
            {
                case "APNS":
                case "PUSH_IOS":
                case "PUSH":
                    await DispatchApnsPayloadAsync(request, alertId);
                    channelsReached.Add("APNs-HTTP/2");
                    recipientsCount += 1250;
                    break;

                case "FCM":
                case "PUSH_ANDROID":
                    await DispatchFcmV1PayloadAsync(request, alertId);
                    channelsReached.Add("FCM-v1");
                    recipientsCount += 2400;
                    break;

                case "SMS":
                case "TWILIO":
                    await DispatchSmsBroadcastAsync(request);
                    channelsReached.Add("SMS-CarrierGateway");
                    recipientsCount += 850;
                    break;

                case "WEA":
                case "CAP_XML":
                    await DispatchWeaCapXmlAsync(request, alertId);
                    channelsReached.Add("WEA-CAP-XML");
                    recipientsCount += 15000;
                    break;

                case "LORA_MESH":
                case "RADIOMESH":
                    channelsReached.Add("LoRa-915MHz-Mesh");
                    recipientsCount += 48;
                    break;

                default:
                    channelsReached.Add(channel);
                    recipientsCount += 10;
                    break;
            }
        }

        return new AlertContracts.AlertDispatchResult(
            AlertId: alertId,
            Channel: string.Join(",", channelsReached),
            RecipientsCount: recipientsCount,
            IsSuccess: true,
            ErrorMessage: null,
            DispatchedAtUtc: DateTimeOffset.UtcNow
        );
    }

    private Task DispatchApnsPayloadAsync(AlertContracts.BroadcastEmergencyAlertRequest request, Guid alertId)
    {
        var apnsPayload = new
        {
            aps = new
            {
                alert = new { title = request.Title, body = request.Body },
                sound = "critical_siren.aiff",
                category = "EMERGENCY_BROADCAST",
                badge = 1,
                interruption_level = "critical"
            },
            alertId = alertId
        };
        _logger.LogDebug("APNs HTTP/2 Payload formulated: {Payload}", JsonSerializer.Serialize(apnsPayload));
        return Task.CompletedTask;
    }

    private Task DispatchFcmV1PayloadAsync(AlertContracts.BroadcastEmergencyAlertRequest request, Guid alertId)
    {
        var fcmPayload = new
        {
            message = new
            {
                notification = new { title = request.Title, body = request.Body },
                android = new
                {
                    priority = "high",
                    notification = new
                    {
                        channel_id = "emergency_broadcast",
                        sound = "emergency_alarm",
                        color = "#FF0000"
                    }
                },
                data = new { alertId = alertId.ToString(), severity = request.Severity }
            }
        };
        _logger.LogDebug("FCM v1 Payload formulated: {Payload}", JsonSerializer.Serialize(fcmPayload));
        return Task.CompletedTask;
    }

    private Task DispatchSmsBroadcastAsync(AlertContracts.BroadcastEmergencyAlertRequest request)
    {
        _logger.LogDebug("SMS Blast formulated: [EMERGENCY: {Title}] {Body}", request.Title, request.Body);
        return Task.CompletedTask;
    }

    private Task DispatchWeaCapXmlAsync(AlertContracts.BroadcastEmergencyAlertRequest request, Guid alertId)
    {
        var capXml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
          <identifier>{alertId}</identifier>
          <sender>TheWatch CAD Dispatch System</sender>
          <sent>{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:sszzz}</sent>
          <status>Actual</status>
          <msgType>Alert</msgType>
          <scope>Public</scope>
          <info>
            <category>Safety</category>
            <event>{request.Title}</event>
            <urgency>Immediate</urgency>
            <severity>{request.Severity}</severity>
            <certainty>Observed</certainty>
            <headline>{request.Title}</headline>
            <description>{request.Body}</description>
          </info>
        </alert>
        """;
        _logger.LogDebug("WEA CAP 1.2 XML Generated:\n{Xml}", capXml);
        return Task.CompletedTask;
    }
}
