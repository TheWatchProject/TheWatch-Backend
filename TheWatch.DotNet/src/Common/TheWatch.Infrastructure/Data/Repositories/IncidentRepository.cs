using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for incident operations.
/// </summary>
public class IncidentRepository : IIncidentRepository
{
    private readonly WatchDbContext _context;

    public IncidentRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<Incident?> GetByIdAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .Include(i => i.Summoner)
            .Include(i => i.ResponderAssignments)
            .Include(i => i.Evidence)
            .FirstOrDefaultAsync(i => i.IncidentId == incidentId, cancellationToken);
    }

    public async Task<Incident> CreateAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        _context.Incidents.Add(incident);
        await _context.SaveChangesAsync(cancellationToken);
        return incident;
    }

    public async Task<Incident> UpdateAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        _context.Incidents.Update(incident);
        await _context.SaveChangesAsync(cancellationToken);
        return incident;
    }

    public async Task<IEnumerable<Incident>> GetActiveIncidentsByGeohashAsync(
        string geohashPrefix,
        CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .Where(i => i.LocationGeohash.StartsWith(geohashPrefix))
            .Where(i => i.Status != "resolved" && i.Status != "escalation_required")
            .OrderByDescending(i => i.ReportedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetIncidentsAwaitingResponseAsync(
        DateTime cutoffTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .Where(i => i.Status == "awaiting_response" || i.Status == "dispatch_in_progress")
            .Where(i => i.ReportedAt < cutoffTime)
            .Include(i => i.Summoner)
            .Include(i => i.ResponderAssignments)
            .OrderBy(i => i.ReportedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetResolvedIncidentsAsync(
        DateTime cutoffTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .Where(i => i.Status == "resolved")
            .Where(i => i.ResolvedAt != null && i.ResolvedAt < cutoffTime)
            .OrderBy(i => i.ResolvedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetActiveIncidentsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var activeStatuses = new[] { "awaiting_response", "dispatch_in_progress", "responder_en_route", "responder_on_scene", "in_progress" };

        // Find incidents where user is either the summoner or an assigned responder
        var summonerIncidents = _context.Incidents
            .Where(i => i.SummonerId == userId)
            .Where(i => activeStatuses.Contains(i.Status));

        var responderIncidents = _context.Incidents
            .Where(i => i.ResponderAssignments.Any(ra => ra.ResponderId == userId && ra.Status != "declined" && ra.Status != "cancelled"))
            .Where(i => activeStatuses.Contains(i.Status));

        return await summonerIncidents
            .Union(responderIncidents)
            .Distinct()
            .OrderByDescending(i => i.ReportedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var summonerIncidents = _context.Incidents
            .Where(i => i.SummonerId == userId);

        var responderIncidents = _context.Incidents
            .Where(i => i.ResponderAssignments.Any(ra => ra.ResponderId == userId));

        return await summonerIncidents
            .Union(responderIncidents)
            .Distinct()
            .OrderByDescending(i => i.ReportedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetActiveIncidentsAsync(
        string? severity = null,
        bool? hasDisagreement = null,
        bool? hasDistress = null,
        int limit = 50,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .Where(i => i.Status != "resolved" && i.Status != "closed" && i.Status != "cancelled")
            .Include(i => i.Summoner)
            .Include(i => i.ResponderAssignments)
            .Include(i => i.Evidence)
            .Include(i => i.TimelineEvents)
            .Include(i => i.Disagreements)
            .AsQueryable();

        if (hasDisagreement.HasValue && hasDisagreement.Value)
        {
            query = query.Where(i => i.Disagreements.Any());
        }

        if (hasDistress.HasValue && hasDistress.Value)
        {
            query = query.Where(i => i.DuressFlag || i.EscalatedToPolice);
        }

        if (limit <= 0) limit = 50;

        return await query
            .OrderByDescending(i => i.ReportedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
