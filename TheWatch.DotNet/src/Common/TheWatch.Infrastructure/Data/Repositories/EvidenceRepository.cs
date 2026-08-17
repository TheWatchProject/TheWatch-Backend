using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for evidence operations.
/// Includes chain of custody tracking for legal proceedings.
/// </summary>
public class EvidenceRepository : IEvidenceRepository
{
    private readonly WatchDbContext _context;

    public EvidenceRepository(WatchDbContext context)
    {
        _context = context;
    }

    public async Task<Evidence?> GetByIdAsync(Guid evidenceId, CancellationToken cancellationToken = default)
    {
        return await _context.EvidenceRecords
            .FirstOrDefaultAsync(e => e.EvidenceId == evidenceId, cancellationToken);
    }

    public async Task<IEnumerable<Evidence>> GetByIncidentIdAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await _context.EvidenceRecords
            .Where(e => e.IncidentId == incidentId)
            .OrderBy(e => e.UploadTimestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Evidence>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.EvidenceRecords
            .Where(e => e.UploadedByResponderId == userId)
            .OrderByDescending(e => e.UploadTimestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<Evidence> CreateAsync(Evidence evidence, CancellationToken cancellationToken = default)
    {
        if (evidence.EvidenceId == Guid.Empty)
            evidence.EvidenceId = Guid.NewGuid();
        evidence.UploadTimestamp = DateTime.UtcNow;

        _context.EvidenceRecords.Add(evidence);
        await _context.SaveChangesAsync(cancellationToken);

        // Log chain of custody event
        await LogChainOfCustodyEventAsync(evidence.EvidenceId, evidence.UploadedByResponderId, 
            "Upload", "Evidence uploaded", cancellationToken);

        return evidence;
    }

    public async Task UpdateAsync(Evidence evidence, CancellationToken cancellationToken = default)
    {
        _context.EvidenceRecords.Update(evidence);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> VerifyIntegrityAsync(Guid evidenceId, CancellationToken cancellationToken = default)
    {
        var evidence = await GetByIdAsync(evidenceId, cancellationToken);
        if (evidence == null) return false;

        // In real implementation, would re-compute hash and compare
        // For now, return true if hash exists
        return !string.IsNullOrEmpty(evidence.Sha256Hash);
    }

    // Legal hold operations

    public async Task PlaceLegalHoldAsync(Guid evidenceId, string caseNumber, Guid placedById, CancellationToken cancellationToken = default)
    {
        var evidence = await _context.EvidenceRecords.FindAsync(new object[] { evidenceId }, cancellationToken);
        if (evidence == null) return;

        evidence.LegalHold = true;
        evidence.LegalHoldPlacedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Log chain of custody event
        await LogChainOfCustodyEventAsync(evidenceId, placedById, "LegalHoldPlaced", 
            $"Legal hold placed for case: {caseNumber}", cancellationToken);
    }

    public async Task ReleaseLegalHoldAsync(Guid evidenceId, Guid releasedById, CancellationToken cancellationToken = default)
    {
        var evidence = await _context.EvidenceRecords.FindAsync(new object[] { evidenceId }, cancellationToken);
        if (evidence == null) return;

        evidence.LegalHold = false;

        await _context.SaveChangesAsync(cancellationToken);

        // Log chain of custody event
        await LogChainOfCustodyEventAsync(evidenceId, releasedById, "LegalHoldReleased", 
            "Legal hold released", cancellationToken);
    }

    public async Task<IEnumerable<Evidence>> GetEvidenceUnderLegalHoldAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EvidenceRecords
            .Where(e => e.LegalHold)
            .OrderBy(e => e.LegalHoldPlacedAt)
            .ToListAsync(cancellationToken);
    }

    // Chain of custody

    public async Task LogChainOfCustodyEventAsync(Guid evidenceId, Guid actorId, string eventType, 
        string? details = null, CancellationToken cancellationToken = default)
    {
        var custodyEvent = new EvidenceChainOfCustody
        {
            CustodyEventId = Guid.NewGuid(),
            EvidenceId = evidenceId,
            EventType = eventType,
            ActorId = actorId,
            Details = details,
            Timestamp = DateTime.UtcNow
        };

        _context.EvidenceChainOfCustody.Add(custodyEvent);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task LogAccessAsync(Guid evidenceId, Guid accessorId, string reason, CancellationToken cancellationToken = default)
    {
        await LogChainOfCustodyEventAsync(evidenceId, accessorId, "Access", reason, cancellationToken);
    }

    public async Task<IEnumerable<EvidenceChainOfCustody>> GetChainOfCustodyAsync(Guid evidenceId, CancellationToken cancellationToken = default)
    {
        return await _context.EvidenceChainOfCustody
            .Where(c => c.EvidenceId == evidenceId)
            .OrderBy(c => c.Timestamp)
            .ToListAsync(cancellationToken);
    }

    // Retention

    public async Task<IEnumerable<Evidence>> GetEvidenceForRetentionCleanupAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        return await _context.EvidenceRecords
            .Where(e => !e.LegalHold && e.UploadTimestamp < cutoffDate)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsDeletedAsync(Guid evidenceId, Guid deletedById, CancellationToken cancellationToken = default)
    {
        // Log chain of custody event for deletion
        await LogChainOfCustodyEventAsync(evidenceId, deletedById, "Deleted", "Evidence marked for deletion", cancellationToken);

        // Note: The Evidence entity doesn't have DeletedAt/DeletedById properties,
        // so we just log the chain of custody event. Actual deletion would be handled
        // by a separate cleanup process.
    }
}
