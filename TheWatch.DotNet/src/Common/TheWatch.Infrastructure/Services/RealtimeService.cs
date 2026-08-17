using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Infrastructure.Data;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of real-time SignalR communication service.
/// Manages subscriptions and broadcasts events to connected clients.
/// </summary>
public class RealtimeService : IRealtimeService
{
    private readonly WatchDbContext _context;
    // NOTE: In production, inject IHubContext<TheWatchHub> from Microsoft.AspNetCore.SignalR
    // For now, this is a stub implementation that manages subscriptions in the database

    public RealtimeService(WatchDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<SignalRSubscription> SubscribeToIncidentAsync(
        Guid userId,
        Guid incidentId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = new SignalRSubscription
        {
            SubscriptionId = Guid.NewGuid(),
            UserId = userId,
            SubscriptionType = "incident",
            TargetId = incidentId,
            ConnectionId = connectionId,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.SignalRSubscriptions.Add(subscription);
        await _context.SaveChangesAsync(cancellationToken);

        return subscription;
    }

    public async Task UnsubscribeFromIncidentAsync(
        Guid userId,
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await _context.SignalRSubscriptions
            .Where(s => s.UserId == userId &&
                       s.SubscriptionType == "incident" &&
                       s.TargetId == incidentId)
            .ToListAsync(cancellationToken);

        _context.SignalRSubscriptions.RemoveRange(subscriptions);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<SignalRSubscription> SubscribeToHqBroadcastsAsync(
        Guid userId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = new SignalRSubscription
        {
            SubscriptionId = Guid.NewGuid(),
            UserId = userId,
            SubscriptionType = "hq_broadcast",
            TargetId = null,
            ConnectionId = connectionId,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.SignalRSubscriptions.Add(subscription);
        await _context.SaveChangesAsync(cancellationToken);

        return subscription;
    }

    public async Task UnsubscribeFromHqBroadcastsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await _context.SignalRSubscriptions
            .Where(s => s.UserId == userId && s.SubscriptionType == "hq_broadcast")
            .ToListAsync(cancellationToken);

        _context.SignalRSubscriptions.RemoveRange(subscriptions);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<SignalRSubscription> SubscribeToEvacuationAsync(
        Guid userId,
        Guid evacuationId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = new SignalRSubscription
        {
            SubscriptionId = Guid.NewGuid(),
            UserId = userId,
            SubscriptionType = "evacuation",
            TargetId = evacuationId,
            ConnectionId = connectionId,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.SignalRSubscriptions.Add(subscription);
        await _context.SaveChangesAsync(cancellationToken);

        return subscription;
    }

    public async Task BroadcastIncidentEventAsync(
        Guid incidentId,
        string eventType,
        object data,
        CancellationToken cancellationToken = default)
    {
        // Get all active subscriptions for this incident
        var subscriptions = await _context.SignalRSubscriptions
            .Where(s => s.SubscriptionType == "incident" &&
                       s.TargetId == incidentId &&
                       s.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        // TODO: In production, use IHubContext to send messages:
        // await _hubContext.Clients.Clients(subscriptions.Select(s => s.ConnectionId))
        //     .SendAsync("IncidentEvent", new { incidentId, eventType, data }, cancellationToken);

        // Update last activity time
        foreach (var sub in subscriptions)
        {
            sub.LastActivityAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BroadcastLocationUpdateAsync(
        Guid sessionId,
        LocationRecord location,
        CancellationToken cancellationToken = default)
    {
        // Get all active subscriptions for location streaming
        var subscriptions = await _context.SignalRSubscriptions
            .Where(s => s.SubscriptionType == "location_stream" &&
                       s.TargetId == sessionId &&
                       s.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        // TODO: In production, use IHubContext to send messages:
        // await _hubContext.Clients.Clients(subscriptions.Select(s => s.ConnectionId))
        //     .SendAsync("LocationUpdate", location, cancellationToken);

        // Update last activity time
        foreach (var sub in subscriptions)
        {
            sub.LastActivityAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<LocationStreamingSession> StartLocationStreamingAsync(
        Guid userId,
        Guid sessionId,
        string sessionType,
        int updateIntervalSeconds = 10,
        CancellationToken cancellationToken = default)
    {
        var session = new LocationStreamingSession
        {
            SessionId = sessionId,
            UserId = userId,
            SessionType = sessionType,
            Status = "active",
            UpdateIntervalSeconds = updateIntervalSeconds,
            TtlSeconds = 3600, // 1 hour
            CreatedAt = DateTime.UtcNow,
            StoppedAt = null
        };

        // TODO: Store in Redis or Cosmos DB with TTL
        // For now, return the session object

        return await Task.FromResult(session);
    }

    public async Task StopLocationStreamingAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        // Remove all location streaming subscriptions for this session
        var subscriptions = await _context.SignalRSubscriptions
            .Where(s => s.SubscriptionType == "location_stream" &&
                       s.TargetId == sessionId &&
                       s.UserId == userId)
            .ToListAsync(cancellationToken);

        _context.SignalRSubscriptions.RemoveRange(subscriptions);
        await _context.SaveChangesAsync(cancellationToken);

        // TODO: Update session status in Redis/Cosmos
    }

    public async Task CleanupExpiredSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var expiredSubscriptions = await _context.SignalRSubscriptions
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        _context.SignalRSubscriptions.RemoveRange(expiredSubscriptions);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
