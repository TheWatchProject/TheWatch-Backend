using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for summoner photo operations.
/// Photos are auto-deleted when incidents close.
/// </summary>
public class SummonerPhotoRepository : ISummonerPhotoRepository
{
    private readonly WatchDbContext _context;

    public SummonerPhotoRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<SummonerPhoto?> GetByIdAsync(Guid photoId, CancellationToken cancellationToken = default)
    {
        return await _context.SummonerPhotos
            .Include(p => p.Incident)
            .Include(p => p.Summoner)
            .FirstOrDefaultAsync(p => p.PhotoId == photoId, cancellationToken);
    }

    public async Task<IEnumerable<SummonerPhoto>> GetPhotosByIncidentIdAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await _context.SummonerPhotos
            .Where(p => p.IncidentId == incidentId)
            .OrderByDescending(p => p.CapturedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<SummonerPhoto> CreateAsync(SummonerPhoto photo, CancellationToken cancellationToken = default)
    {
        _context.SummonerPhotos.Add(photo);
        await _context.SaveChangesAsync(cancellationToken);
        return photo;
    }

    public async Task DeleteAsync(Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await _context.SummonerPhotos.FindAsync(new object[] { photoId }, cancellationToken);
        if (photo != null)
        {
            _context.SummonerPhotos.Remove(photo);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<SummonerPhoto>> GetPhotosForClosedIncidentsAsync(
        DateTime cutoffTime,
        CancellationToken cancellationToken = default)
    {
        var closedStatuses = new[] { "closed", "cancelled" };

        return await _context.SummonerPhotos
            .Include(p => p.Incident)
            .Where(p => p.Incident != null && closedStatuses.Contains(p.Incident.Status))
            .Where(p => p.AutoDeleteAt != null && p.AutoDeleteAt < cutoffTime)
            .OrderBy(p => p.AutoDeleteAt)
            .ToListAsync(cancellationToken);
    }
}
