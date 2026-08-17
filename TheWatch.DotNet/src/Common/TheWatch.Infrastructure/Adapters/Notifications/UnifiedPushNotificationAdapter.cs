using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;
using TheWatch.Core.Notifications;

namespace TheWatch.Infrastructure.Adapters.Notifications;

public class UnifiedPushNotificationAdapter : INotificationPort
{
    private readonly ILogger<UnifiedPushNotificationAdapter> _logger;
    private readonly INotificationDispatcher _dispatcher;

    public UnifiedPushNotificationAdapter(INotificationDispatcher dispatcher, ILogger<UnifiedPushNotificationAdapter> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<bool> SendPushNotificationAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var request = TheWatch.Core.Notifications.NotificationRequest.Create(
            new NotificationRecipient(Address: message.TargetToken),
            message.Title,
            message.Body,
            NotificationChannel.Push,
            data: message.Data,
            respectPreferences: false);
        var result = await _dispatcher.DispatchAsync(request, ct).ConfigureAwait(false);
        _logger.LogInformation("Queued push notification {NotificationId}; accepted channels: {AcceptedCount}", request.Id, result.AcceptedCount);
        return result.HasAccepted;
    }

    public async Task<bool> SendSmsAlertAsync(string phoneNumber, string messageText, CancellationToken ct = default)
    {
        var request = TheWatch.Core.Notifications.NotificationRequest.Create(
            new NotificationRecipient(Address: phoneNumber),
            "Emergency alert",
            messageText,
            NotificationChannel.Sms,
            NotificationPriority.Critical,
            NotificationCategory.Safety,
            respectPreferences: false);
        var result = await _dispatcher.DispatchAsync(request, ct).ConfigureAwait(false);
        _logger.LogInformation("Queued emergency SMS {NotificationId}; accepted channels: {AcceptedCount}", request.Id, result.AcceptedCount);
        return result.HasAccepted;
    }
}
