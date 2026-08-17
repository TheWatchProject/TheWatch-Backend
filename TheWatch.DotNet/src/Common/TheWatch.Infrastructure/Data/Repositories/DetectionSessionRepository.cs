using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for detection session operations.
/// </summary>
public class DetectionSessionRepository : IDetectionSessionRepository
{
    private readonly WatchDbContext _context;

    public DetectionSessionRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<DetectionSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return await _context.DetectionSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
    }

    public async Task<DetectionSession?> GetActiveSessionForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.DetectionSessions
            .Where(s => s.UserId == userId && s.Status == DetectionSessionStatus.Active)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DetectionSession> CreateAsync(DetectionSession session, CancellationToken cancellationToken = default)
    {
        if (session.SessionId == Guid.Empty)
            session.SessionId = Guid.NewGuid();

        session.StartedAt = DateTime.UtcNow;

        _context.DetectionSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return session;
    }

    public async Task<DetectionSession> UpdateAsync(DetectionSession session, CancellationToken cancellationToken = default)
    {
        _context.DetectionSessions.Update(session);
        await _context.SaveChangesAsync(cancellationToken);

        return session;
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _context.DetectionSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session != null)
        {
            _context.DetectionSessions.Remove(session);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> GetActiveSessionCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.DetectionSessions
            .CountAsync(s => s.UserId == userId && s.Status == DetectionSessionStatus.Active, cancellationToken);
    }
}
