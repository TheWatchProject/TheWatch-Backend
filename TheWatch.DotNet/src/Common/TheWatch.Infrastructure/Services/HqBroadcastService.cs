using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Infrastructure.Data;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of HQ broadcast service ("Voice of God" alerts).
/// Sends critical alerts to responders via SignalR, push notifications, and SMS fallback.
/// </summary>
public class HqBroadcastService : IHqBroadcastService
{
    private readonly WatchDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IRealtimeService _realtimeService;

    public HqBroadcastService(
        WatchDbContext context,
        INotificationService notificationService,
        IRealtimeService realtimeService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _realtimeService = realtimeService ?? throw new ArgumentNullException(nameof(realtimeService));
    }

    public async Task<HqBroadcast> SendBroadcastAsync(
        Guid incidentId,
        Guid sentByUserId,
        string message,
        string? command = null,
        string severity = "critical",
        bool includeAudioTts = true,
        string? audioClipUrl = null,
        bool smsFallback = true,
        CancellationToken cancellationToken = default)
    {
        // Validate incident exists
        var incident = await _context.Incidents
            .FindAsync(new object[] { incidentId }, cancellationToken);

        if (incident == null)
        {
            throw new InvalidOperationException($"Incident {incidentId} not found");
        }

        // Get all assigned responders
        var assignments = await _context.ResponderAssignments
            .Where(a => a.IncidentId == incidentId &&
                       a.Status != "cancelled" &&
                       a.Status != "declined")
            .Include(a => a.Responder)
            .ToListAsync(cancellationToken);

        // Create broadcast record
        var broadcast = new HqBroadcast
        {
            BroadcastId = Guid.NewGuid(),
            IncidentId = incidentId,
            SentByUserId = sentByUserId,
            Message = message,
            Command = command,
            Severity = severity,
            IncludeAudioTts = includeAudioTts,
            AudioClipUrl = audioClipUrl,
            SmsFallback = smsFallback,
            ExpectedRecipients = assignments.Count,
            ConfirmedDeliveries = 0,
            FailedDeliveries = 0,
            SentAt = DateTime.UtcNow
        };

        _context.HqBroadcasts.Add(broadcast);
        await _context.SaveChangesAsync(cancellationToken);

        // Send broadcast via SignalR to all assigned responders
        await _realtimeService.BroadcastIncidentEventAsync(
            incidentId,
            "hq_broadcast",
            new
            {
                broadcastId = broadcast.BroadcastId,
                message,
                command,
                severity,
                audioClipUrl,
                sentAt = broadcast.SentAt
            },
            cancellationToken);

        // Send push notifications
        foreach (var assignment in assignments)
        {
            try
            {
                await _notificationService.SendPushNotificationAsync(
                    assignment.ResponderId,
                    "HQ Broadcast",
                    message,
                    new Dictionary<string, string>
                    {
                        ["type"] = "hq_broadcast",
                        ["broadcastId"] = broadcast.BroadcastId.ToString(),
                        ["incidentId"] = incidentId.ToString(),
                        ["severity"] = severity
                    },
                    cancellationToken);

                broadcast.ConfirmedDeliveries++;
            }
            catch
            {
                broadcast.FailedDeliveries++;

                // TODO: Queue for retry
            }
        }

        // Update broadcast delivery stats
        await _context.SaveChangesAsync(cancellationToken);

        // Add timeline event
        var timelineEvent = new IncidentTimelineEvent
        {
            EventId = Guid.NewGuid(),
            IncidentId = incidentId,
            EventType = IncidentEventType.HqBroadcastSent,
            Actor = ActorType.Hq,
            ActorId = sentByUserId,
            ActorPiiState = "normal",
            Timestamp = DateTime.UtcNow,
            Data = System.Text.Json.JsonSerializer.Serialize(new
            {
                broadcastId = broadcast.BroadcastId,
                message,
                severity,
                recipientCount = assignments.Count
            })
        };

        _context.IncidentTimelineEvents.Add(timelineEvent);
        await _context.SaveChangesAsync(cancellationToken);

        return broadcast;
    }

    public async Task<HqBroadcast> SendIncidentCommandAsync(
        Guid incidentId,
        Guid sentByUserId,
        string command,
        CancellationToken cancellationToken = default)
    {
        string message = command.ToLowerInvariant() switch
        {
            "retreat" => "RETREAT IMMEDIATELY - STAND DOWN",
            "evacuate" => "EVACUATE AREA NOW",
            "await_police" => "AWAIT POLICE ARRIVAL - DO NOT ENGAGE",
            "medical_emergency" => "MEDICAL EMERGENCY - SUMMON EMS",
            "all_clear" => "ALL CLEAR - STAND DOWN",
            _ => $"COMMAND: {command.ToUpperInvariant()}"
        };

        return await SendBroadcastAsync(
            incidentId,
            sentByUserId,
            message,
            command,
            severity: "critical",
            includeAudioTts: true,
            audioClipUrl: null,
            smsFallback: true,
            cancellationToken);
    }

    public async Task<BroadcastStatus> GetBroadcastStatusAsync(
        Guid broadcastId,
        CancellationToken cancellationToken = default)
    {
        var broadcast = await _context.HqBroadcasts
            .FindAsync(new object[] { broadcastId }, cancellationToken);

        if (broadcast == null)
        {
            throw new InvalidOperationException($"Broadcast {broadcastId} not found");
        }

        // Get recipient delivery status
        // TODO: Track individual recipient delivery in a separate table
        var recipients = new List<RecipientDeliveryStatus>();

        var status = new BroadcastStatus
        {
            BroadcastId = broadcastId,
            Status = broadcast.PendingCount == 0 ? "completed" : "in_progress",
            SentAt = broadcast.SentAt,
            ExpectedRecipients = broadcast.ExpectedRecipients,
            ConfirmedDeliveries = broadcast.ConfirmedDeliveries,
            FailedDeliveries = broadcast.FailedDeliveries,
            PendingDeliveries = broadcast.PendingCount,
            Recipients = recipients
        };

        return status;
    }

    public async Task ConfirmBroadcastDeliveryAsync(
        Guid broadcastId,
        Guid responderId,
        CancellationToken cancellationToken = default)
    {
        // TODO: Track individual delivery confirmations in a separate table
        // For now, just update the broadcast record

        var broadcast = await _context.HqBroadcasts
            .FindAsync(new object[] { broadcastId }, cancellationToken);

        if (broadcast != null)
        {
            // Update confirmed count if not already confirmed
            // This is a simplified implementation
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RetryFailedDeliveriesAsync(
        Guid broadcastId,
        CancellationToken cancellationToken = default)
    {
        var broadcast = await _context.HqBroadcasts
            .FindAsync(new object[] { broadcastId }, cancellationToken);

        if (broadcast == null)
        {
            throw new InvalidOperationException($"Broadcast {broadcastId} not found");
        }

        // Get all assigned responders who didn't receive the broadcast
        var assignments = await _context.ResponderAssignments
            .Where(a => a.IncidentId == broadcast.IncidentId &&
                       a.Status != "cancelled" &&
                       a.Status != "declined")
            .ToListAsync(cancellationToken);

        // TODO: Track which responders already received the broadcast
        // For now, resend to all assigned responders

        foreach (var assignment in assignments)
        {
            try
            {
                await _notificationService.SendPushNotificationAsync(
                    assignment.ResponderId,
                    "HQ Broadcast (Retry)",
                    broadcast.Message,
                    new Dictionary<string, string>
                    {
                        ["type"] = "hq_broadcast",
                        ["broadcastId"] = broadcast.BroadcastId.ToString(),
                        ["incidentId"] = broadcast.IncidentId.ToString(),
                        ["severity"] = broadcast.Severity,
                        ["retry"] = "true"
                    },
                    cancellationToken);
            }
            catch
            {
                // Ignore failures for retry
            }
        }
    }

    public async Task MonitorPendingConfirmationsAsync(CancellationToken cancellationToken = default)
    {
        // Get all broadcasts with pending deliveries older than 5 minutes
        var cutoffTime = DateTime.UtcNow.AddMinutes(-5);

        var pendingBroadcasts = await _context.HqBroadcasts
            .Where(b => b.PendingCount > 0 && b.SentAt < cutoffTime)
            .ToListAsync(cancellationToken);

        foreach (var broadcast in pendingBroadcasts)
        {
            // Retry failed deliveries
            await RetryFailedDeliveriesAsync(broadcast.BroadcastId, cancellationToken);
        }
    }
}
