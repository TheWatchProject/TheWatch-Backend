using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Infrastructure.Data;

namespace TheWatch.Infrastructure.Data;

/// <summary>
/// Repository implementation for Outbox pattern.
/// Stores events in database for reliable publishing to message bus.
/// </summary>
public class OutboxRepository : IOutboxRepository
{
    private readonly WatchDbContext _context;

    public OutboxRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<OutboxMessage> CreateAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        _context.OutboxMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<IEnumerable<OutboxMessage>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        return await _context.OutboxMessages
            .Where(m => m.Status == "pending" || 
                       (m.Status == "failed" && m.NextRetryAt <= DateTime.UtcNow))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await _context.OutboxMessages.FindAsync(new object[] { messageId }, cancellationToken);
        if (message != null)
        {
            message.Status = "completed";
            message.ProcessedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAsFailedAsync(Guid messageId, string errorMessage, CancellationToken cancellationToken = default)
    {
        var message = await _context.OutboxMessages.FindAsync(new object[] { messageId }, cancellationToken);
        if (message != null)
        {
            message.Status = "failed";
            message.ErrorMessage = errorMessage;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateRetryCountAsync(Guid messageId, int retryCount, DateTime nextRetryAt, CancellationToken cancellationToken = default)
    {
        var message = await _context.OutboxMessages.FindAsync(new object[] { messageId }, cancellationToken);
        if (message != null)
        {
            message.RetryCount = retryCount;
            message.NextRetryAt = nextRetryAt;
            message.Status = "failed";
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
