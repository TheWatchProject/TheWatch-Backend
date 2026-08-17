using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Infrastructure.Data;

namespace TheWatch.Infrastructure.Data;

/// <summary>
/// Repository implementation for Saga pattern orchestration.
/// Manages long-running distributed transactions with compensation logic.
/// </summary>
public class SagaRepository : ISagaRepository
{
    private readonly WatchDbContext _context;

    public SagaRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<SagaInstance> CreateAsync(SagaInstance saga, CancellationToken cancellationToken = default)
    {
        _context.SagaInstances.Add(saga);
        await _context.SaveChangesAsync(cancellationToken);
        return saga;
    }

    public async Task<SagaInstance?> GetByIdAsync(Guid sagaInstanceId, CancellationToken cancellationToken = default)
    {
        return await _context.SagaInstances.FindAsync(new object[] { sagaInstanceId }, cancellationToken);
    }

    public async Task<IEnumerable<SagaInstance>> GetInProgressSagasAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SagaInstances
            .Where(s => s.Status == "started" || s.Status == "in_progress")
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(SagaInstance saga, CancellationToken cancellationToken = default)
    {
        saga.LastUpdatedAt = DateTime.UtcNow;
        _context.SagaInstances.Update(saga);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAsCompletedAsync(Guid sagaInstanceId, CancellationToken cancellationToken = default)
    {
        var saga = await GetByIdAsync(sagaInstanceId, cancellationToken);
        if (saga != null)
        {
            saga.Status = "completed";
            saga.CompletedAt = DateTime.UtcNow;
            saga.LastUpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAsFailedAsync(Guid sagaInstanceId, string errorMessage, CancellationToken cancellationToken = default)
    {
        var saga = await GetByIdAsync(sagaInstanceId, cancellationToken);
        if (saga != null)
        {
            saga.Status = "failed";
            saga.ErrorMessage = errorMessage;
            saga.LastUpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
