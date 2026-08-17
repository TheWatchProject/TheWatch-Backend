namespace TheWatch.Microservices.Notifications.NotificationService.Models;

public enum NotificationChannel
{
    PushNotification,
    Sms,
    Email,
    TacticalRadio,
    Webhook,
    SirensAndStrobe
}

public enum NotificationPriority
{
    Low,
    Normal,
    High,
    Urgent,
    FlashEmergency
}

public enum DeliveryStatus
{
    Queued,
    Sent,
    Delivered,
    Failed,
    Acknowledged
}

public class NotificationMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RecipientId { get; set; } = string.Empty;
    public string RecipientContact { get; set; } = string.Empty; // phone, email, or device token
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; } = NotificationChannel.PushNotification;
    public NotificationPriority Priority { get; set; } = NotificationPriority.High;
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Sent;
    public string? IncidentId { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAtUtc { get; set; }
}

public class SendNotificationRequest
{
    public string RecipientId { get; set; } = string.Empty;
    public string RecipientContact { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; } = NotificationChannel.PushNotification;
    public NotificationPriority Priority { get; set; } = NotificationPriority.High;
    public string? IncidentId { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class BroadcastAlertRequest
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TargetSector { get; set; } = "ALL_UNITS";
    public NotificationPriority Priority { get; set; } = NotificationPriority.FlashEmergency;
    public string? IncidentId { get; set; }
    public List<NotificationChannel> Channels { get; set; } = new() { NotificationChannel.PushNotification, NotificationChannel.TacticalRadio };
}

public class SubscriptionRequest
{
    public string UserId { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
    public string Platform { get; set; } = "iOS"; // iOS, Android, Web, Desktop
    public List<string> SubscribedTopics { get; set; } = new();
}

public class BroadcastResult
{
    public string BroadcastId { get; set; } = Guid.NewGuid().ToString();
    public int RecipientsTargeted { get; set; }
    public int SuccessfulDeliveries { get; set; }
    public DateTime DispatchedAtUtc { get; set; } = DateTime.UtcNow;
}
