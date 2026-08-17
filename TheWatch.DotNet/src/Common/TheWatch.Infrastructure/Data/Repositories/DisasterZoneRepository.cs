using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for disaster zone operations.
/// </summary>
public class DisasterZoneRepository : IDisasterZoneRepository
{
    private readonly WatchDbContext _context;

    public DisasterZoneRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<DisasterZone?> GetByIdAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        return await _context.DisasterZones
            .FirstOrDefaultAsync(z => z.ZoneId == zoneId, cancellationToken);
    }

    public async Task<IEnumerable<DisasterZone>> GetActiveZonesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DisasterZones
            .Where(z => z.ExpiresAt == null || z.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(z => z.Severity)
            .ThenByDescending(z => z.IssuedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<DisasterZone>> GetZonesContainingGeohashAsync(string geohash, CancellationToken cancellationToken = default)
    {
        // Find zones where the center geohash prefix matches
        var activeZones = await GetActiveZonesAsync(cancellationToken);
        
        return activeZones.Where(z => 
            !string.IsNullOrEmpty(z.CenterGeohash) && geohash.StartsWith(z.CenterGeohash.Substring(0, Math.Min(4, z.CenterGeohash.Length)))
        ).ToList();
    }

    public async Task<IEnumerable<DisasterZone>> GetZonesByTypeAsync(string disasterType, CancellationToken cancellationToken = default)
    {
        return await _context.DisasterZones
            .Where(z => z.DisasterType == disasterType && (z.ExpiresAt == null || z.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(z => z.IssuedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<DisasterZone> CreateAsync(DisasterZone zone, CancellationToken cancellationToken = default)
    {
        if (zone.ZoneId == Guid.Empty)
            zone.ZoneId = Guid.NewGuid();
        zone.IssuedAt = DateTime.UtcNow;
        zone.CreatedAt = DateTime.UtcNow;
        zone.UpdatedAt = DateTime.UtcNow;

        _context.DisasterZones.Add(zone);
        await _context.SaveChangesAsync(cancellationToken);

        return zone;
    }

    public async Task UpdateAsync(DisasterZone zone, CancellationToken cancellationToken = default)
    {
        zone.UpdatedAt = DateTime.UtcNow;
        _context.DisasterZones.Update(zone);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSeverityAsync(Guid zoneId, string severity, CancellationToken cancellationToken = default)
    {
        var zone = await _context.DisasterZones.FindAsync(new object[] { zoneId }, cancellationToken);
        if (zone != null)
        {
            zone.Severity = severity;
            zone.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateEvacuationOrderAsync(Guid zoneId, string order, CancellationToken cancellationToken = default)
    {
        var zone = await _context.DisasterZones.FindAsync(new object[] { zoneId }, cancellationToken);
        if (zone != null)
        {
            zone.EvacuationOrder = order;
            zone.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ExpireAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        var zone = await _context.DisasterZones.FindAsync(new object[] { zoneId }, cancellationToken);
        if (zone != null)
        {
            zone.ExpiresAt = DateTime.UtcNow;
            zone.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> GetAffectedUserCountAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        var zone = await GetByIdAsync(zoneId, cancellationToken);
        if (zone == null) return 0;

        // Return the estimated population if available
        return zone.AffectedPopulationEstimate ?? 0;
    }

    public async Task<IEnumerable<DisasterZone>> GetActiveZonesAsync(
        string? disasterType,
        string? severity,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DisasterZones
            .Where(z => z.IsActive && (z.ExpiresAt == null || z.ExpiresAt > DateTime.UtcNow));

        if (!string.IsNullOrEmpty(disasterType))
            query = query.Where(z => z.DisasterType == disasterType);

        if (!string.IsNullOrEmpty(severity))
            query = query.Where(z => z.Severity == severity);

        return await query
            .OrderByDescending(z => z.Severity)
            .ThenByDescending(z => z.IssuedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<DisasterZone>> GetExpiredActiveZonesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DisasterZones
            .Where(z => z.IsActive && z.ExpiresAt != null && z.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);
    }

    public async Task DeactivateAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        var zone = await _context.DisasterZones.FindAsync(new object[] { zoneId }, cancellationToken);
        if (zone != null)
        {
            zone.IsActive = false;
            zone.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<Guid>> GetUserIdsInGeohashPrefixesAsync(
        IEnumerable<string> geohashPrefixes,
        CancellationToken cancellationToken = default)
    {
        var prefixList = geohashPrefixes.ToList();

        // Get users whose last known location geohash starts with any of the prefixes
        // This would typically query the LocationRecord or User table
        var users = await _context.Users
            .Where(u => u.LastKnownGeohash != null && 
                        prefixList.Any(p => u.LastKnownGeohash.StartsWith(p)))
            .Select(u => u.UserId)
            .ToListAsync(cancellationToken);

        return users;
    }

    public async Task<IEnumerable<DisasterZone>> GetActiveZonesByTypesAsync(
        IEnumerable<string> disasterTypes,
        CancellationToken cancellationToken = default)
    {
        var typeList = disasterTypes.ToList();

        return await _context.DisasterZones
            .Where(z => z.IsActive && 
                        (z.ExpiresAt == null || z.ExpiresAt > DateTime.UtcNow) &&
                        typeList.Contains(z.DisasterType))
            .OrderByDescending(z => z.Severity)
            .ThenByDescending(z => z.IssuedAt)
            .ToListAsync(cancellationToken);
    }
}
