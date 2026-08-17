using System.Collections.Concurrent;
using TheWatch.Microservices.Notifications.NotificationService.Models;

namespace TheWatch.Microservices.Notifications.NotificationService.Services;

public interface INotificationService
{
    Task<NotificationMessage> SendAsync(SendNotificationRequest request);
    Task<BroadcastResult> BroadcastAsync(BroadcastAlertRequest request);
    Task<IEnumerable<NotificationMessage>> GetHistoryAsync(string? recipientId = null, string? incidentId = null, int limit = 50);
    Task<bool> AcknowledgeNotificationAsync(string notificationId);
    Task<bool> RegisterSubscriptionAsync(SubscriptionRequest request);
}

public class NotificationServiceImplementation : INotificationService
{
    private static readonly ConcurrentDictionary<string, NotificationMessage> Notifications = new();
    private static readonly ConcurrentDictionary<string, SubscriptionRequest> Subscriptions = new();

    static NotificationServiceImplementation()
    {
        // Seed initial notifications
        var n1 = new NotificationMessage
        {
            Id = "NOTIF-101",
            RecipientId = "UNIT-MEDIC-42",
            RecipientContact = "push_token_ios_medic42",
            Title = "🚨 PRIORITY DISPATCH: Mass Casualty on I-95",
            Body = "Reported multi-vehicle collision with 4 trapped victims. Respond Code 3 immediately.",
            Channel = NotificationChannel.PushNotification,
            Priority = NotificationPriority.FlashEmergency,
            Status = DeliveryStatus.Delivered,
            IncidentId = "INC-1001",
            SentAtUtc = DateTime.UtcNow.AddMinutes(-24)
        };

        var n2 = new NotificationMessage
        {
            Id = "NOTIF-102",
            RecipientId = "UNIT-FIRE-07",
            RecipientContact = "+15550149",
            Title = "🔥 2nd Alarm Fire Dispatched",
            Body = "Commercial structure fire at 742 Evergreen Terrace. Heavy smoke condition.",
            Channel = NotificationChannel.TacticalRadio,
            Priority = NotificationPriority.Urgent,
            Status = DeliveryStatus.Delivered,
            IncidentId = "INC-1002",
            SentAtUtc = DateTime.UtcNow.AddMinutes(-11)
        };

        Notifications[n1.Id] = n1;
        Notifications[n2.Id] = n2;
    }

    public Task<NotificationMessage> SendAsync(SendNotificationRequest request)
    {
        var msg = new NotificationMessage
        {
            Id = $"NOTIF-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            RecipientId = request.RecipientId,
            RecipientContact = request.RecipientContact,
            Title = request.Title,
            Body = request.Body,
            Channel = request.Channel,
            Priority = request.Priority,
            Status = DeliveryStatus.Delivered,
            IncidentId = request.IncidentId,
            Metadata = request.Metadata ?? new Dictionary<string, string>(),
            SentAtUtc = DateTime.UtcNow
        };

        Notifications[msg.Id] = msg;
        return Task.FromResult(msg);
    }

    public Task<BroadcastResult> BroadcastAsync(BroadcastAlertRequest request)
    {
        var broadcastId = $"BCAST-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var totalTargeted = Math.Max(Subscriptions.Count, 8); // at least all standard active squad units

        // Create broadcast entry in notifications history
        var record = new NotificationMessage
        {
            Id = broadcastId,
            RecipientId = $"BROADCAST-{request.TargetSector}",
            RecipientContact = "ALL_ACTIVE_FIELD_CHANNELS",
            Title = request.Title,
            Body = request.Message,
            Channel = request.Channels.FirstOrDefault(),
            Priority = request.Priority,
            Status = DeliveryStatus.Delivered,
            IncidentId = request.IncidentId,
            Metadata = new Dictionary<string, string>
            {
                ["TargetSector"] = request.TargetSector,
                ["Channels"] = string.Join(",", request.Channels)
            },
            SentAtUtc = DateTime.UtcNow
        };

        Notifications[record.Id] = record;

        return Task.FromResult(new BroadcastResult
        {
            BroadcastId = broadcastId,
            RecipientsTargeted = totalTargeted,
            SuccessfulDeliveries = totalTargeted,
            DispatchedAtUtc = DateTime.UtcNow
        });
    }

    public Task<IEnumerable<NotificationMessage>> GetHistoryAsync(string? recipientId = null, string? incidentId = null, int limit = 50)
    {
        IEnumerable<NotificationMessage> list = Notifications.Values;

        if (!string.IsNullOrWhiteSpace(recipientId))
            list = list.Where(n => n.RecipientId.Equals(recipientId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(incidentId))
            list = list.Where(n => n.IncidentId != null && n.IncidentId.Equals(incidentId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(list.OrderByDescending(n => n.SentAtUtc).Take(limit).AsEnumerable());
    }

    public Task<bool> AcknowledgeNotificationAsync(string notificationId)
    {
        if (!Notifications.TryGetValue(notificationId, out var msg))
            return Task.FromResult(false);

        msg.Status = DeliveryStatus.Acknowledged;
        msg.AcknowledgedAtUtc = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<bool> RegisterSubscriptionAsync(SubscriptionRequest request)
    {
        Subscriptions[request.UserId] = request;
        return Task.FromResult(true);
    }
}
