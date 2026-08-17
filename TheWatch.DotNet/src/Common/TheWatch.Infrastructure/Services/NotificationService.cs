using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Notifications;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Maps domain notification operations to the generated, channel-neutral dispatcher.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(INotificationDispatcher dispatcher, ILogger<NotificationService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendDispatchNotificationAsync(Guid responderId, Guid incidentId, CancellationToken cancellationToken = default) =>
        DispatchAsync(
            new NotificationRecipient(UserId: responderId),
            "Emergency dispatch",
            $"You have a new incident assignment ({incidentId:N}).",
            NotificationCategory.Dispatch,
            NotificationPriority.Critical,
            Data(("incidentId", incidentId.ToString("D"))),
            respectPreferences: false,
            cancellationToken);

    /// <inheritdoc />
    public Task SendDispatchNotificationAsync(Guid responderId, Guid incidentId, string emergencyType, string severity, CancellationToken cancellationToken = default) =>
        DispatchAsync(
            new NotificationRecipient(UserId: responderId),
            $"{severity} emergency dispatch",
            $"{emergencyType} incident assignment ({incidentId:N}).",
            NotificationCategory.Dispatch,
            ParsePriority(severity),
            Data(("incidentId", incidentId.ToString("D")), ("emergencyType", emergencyType), ("severity", severity)),
            respectPreferences: false,
            cancellationToken);

    /// <inheritdoc />
    public Task SendHqBroadcastAsync(string message, Guid incidentId, CancellationToken cancellationToken = default) =>
        DispatchAsync(
            new NotificationRecipient(Topic: $"incident/{incidentId:N}"),
            "HQ broadcast",
            message,
            NotificationCategory.Incident,
            NotificationPriority.Critical,
            Data(("incidentId", incidentId.ToString("D"))),
            respectPreferences: false,
            cancellationToken);

    /// <inheritdoc />
    public Task SendHqBroadcastAsync(string type, string message, string action, Dictionary<string, string> data, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string>(data, StringComparer.Ordinal)
        {
            ["type"] = type,
            ["action"] = action,
        };
        return DispatchAsync(new NotificationRecipient(Topic: "hq"), type, message, NotificationCategory.System, NotificationPriority.Critical, payload, false, cancellationToken);
    }

    /// <inheritdoc />
    public Task SendResponderDistressAlertAsync(Guid responderId, Guid incidentId, CancellationToken cancellationToken = default) =>
        DispatchAsync(
            new NotificationRecipient(Topic: "hq"),
            "Responder distress alert",
            $"Responder {responderId:N} requested immediate assistance at incident {incidentId:N}.",
            NotificationCategory.Safety,
            NotificationPriority.Critical,
            Data(("responderId", responderId.ToString("D")), ("incidentId", incidentId.ToString("D"))),
            false,
            cancellationToken);

    /// <inheritdoc />
    public Task SendPushNotificationAsync(Guid userId, string title, string message, Dictionary<string, string>? data = null, CancellationToken cancellationToken = default) =>
        DispatchAsync(new NotificationRecipient(UserId: userId), title, message, NotificationCategory.General, NotificationPriority.Normal, data, true, cancellationToken);

    /// <inheritdoc />
    public Task BroadcastIncidentUpdateAsync(Guid incidentId, object update, CancellationToken cancellationToken = default) =>
        DispatchAsync(
            new NotificationRecipient(Topic: $"incident/{incidentId:N}"),
            "Incident update",
            JsonSerializer.Serialize(update),
            NotificationCategory.Incident,
            NotificationPriority.High,
            Data(("incidentId", incidentId.ToString("D"))),
            false,
            cancellationToken);

    /// <inheritdoc />
    public async Task<(int sent, int failed)> SendDisasterZoneNotificationAsync(
        IEnumerable<Guid> userIds,
        Guid zoneId,
        string title,
        string message,
        string priority,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        var sent = 0;
        var failed = 0;
        foreach (var userId in userIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = data is null ? new Dictionary<string, string>() : new Dictionary<string, string>(data, StringComparer.Ordinal);
            payload["zoneId"] = zoneId.ToString("D");
            var summary = await DispatchCoreAsync(new NotificationRecipient(UserId: userId), title, message, NotificationCategory.Disaster, ParsePriority(priority), payload, false, cancellationToken).ConfigureAwait(false);
            if (summary.HasAccepted) sent++; else failed++;
        }
        return (sent, failed);
    }

    /// <inheritdoc />
    public Task<(int sent, int failed)> SendDisasterZoneAllClearAsync(IEnumerable<Guid> userIds, Guid zoneId, string zoneName, CancellationToken cancellationToken = default) =>
        SendDisasterZoneNotificationAsync(userIds, zoneId, $"All clear: {zoneName}", "The disaster-zone alert has been lifted.", "normal", cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task SendIncidentNotificationAsync(Guid userId, Guid incidentId, string notificationType, string message, CancellationToken cancellationToken = default) =>
        DispatchAsync(
            new NotificationRecipient(UserId: userId),
            notificationType,
            message,
            NotificationCategory.Incident,
            NotificationPriority.High,
            Data(("incidentId", incidentId.ToString("D")), ("notificationType", notificationType)),
            true,
            cancellationToken);

    /// <inheritdoc />
    public Task SendHqAlertAsync(string title, string message, string severity, CancellationToken cancellationToken = default) =>
        DispatchAsync(new NotificationRecipient(Topic: "hq"), title, message, NotificationCategory.System, ParsePriority(severity), null, false, cancellationToken);

    private async Task DispatchAsync(
        NotificationRecipient recipient,
        string title,
        string body,
        NotificationCategory category,
        NotificationPriority priority,
        IReadOnlyDictionary<string, string>? data,
        bool respectPreferences,
        CancellationToken cancellationToken)
    {
        var summary = await DispatchCoreAsync(recipient, title, body, category, priority, data, respectPreferences, cancellationToken).ConfigureAwait(false);
        if (!summary.HasAccepted)
        {
            _logger.LogWarning(
                "Notification {NotificationId} had no accepted delivery channels; failures: {FailedCount}",
                summary.Request.Id,
                summary.FailedCount);
        }
    }

    private ValueTask<NotificationDispatchSummary> DispatchCoreAsync(
        NotificationRecipient recipient,
        string title,
        string body,
        NotificationCategory category,
        NotificationPriority priority,
        IReadOnlyDictionary<string, string>? data,
        bool respectPreferences,
        CancellationToken cancellationToken)
    {
        var request = NotificationRequest.Create(recipient, title, body, NotificationChannel.Push, priority, category, data, respectPreferences);
        return _dispatcher.DispatchAsync(request, cancellationToken);
    }

    private static NotificationPriority ParsePriority(string value) => value.Trim().ToLowerInvariant() switch
    {
        "critical" or "emergency" => NotificationPriority.Critical,
        "high" or "severe" => NotificationPriority.High,
        "low" => NotificationPriority.Low,
        _ => NotificationPriority.Normal,
    };

    private static Dictionary<string, string> Data(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
}
