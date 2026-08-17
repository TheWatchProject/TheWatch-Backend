using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for evacuation operations.
/// </summary>
public class EvacuationRepository : IEvacuationRepository
{
    private readonly WatchDbContext _context;

    public EvacuationRepository(WatchDbContext context)
    {
        _context = context;
    }

    // Evacuation Request operations

    public async Task<EvacuationRequest?> GetRequestByIdAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationRequests
            .Include(r => r.Evacuee)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
    }

    public async Task<IEnumerable<EvacuationRequest>> GetActiveRequestsInAreaAsync(string geohashPrefix, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationRequests
            .Where(r => r.CurrentLocationGeohash.StartsWith(geohashPrefix) 
                && r.Status != "completed" 
                && r.Status != "cancelled"
                && r.Status != "expired")
            .OrderBy(r => r.Urgency)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<EvacuationRequest>> GetUserRequestsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationRequests
            .Where(r => r.EvacueeId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<EvacuationRequest> CreateRequestAsync(EvacuationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RequestId == Guid.Empty)
            request.RequestId = Guid.NewGuid();
        request.Status = "pending";
        request.CreatedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        _context.EvacuationRequests.Add(request);
        await _context.SaveChangesAsync(cancellationToken);

        return request;
    }

    public async Task UpdateRequestStatusAsync(Guid requestId, string status, CancellationToken cancellationToken = default)
    {
        var request = await _context.EvacuationRequests.FindAsync(new object[] { requestId }, cancellationToken);
        if (request != null)
        {
            request.Status = status;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // Shelter operations

    public async Task<TemporaryShelter?> GetShelterByIdAsync(Guid shelterId, CancellationToken cancellationToken = default)
    {
        return await _context.TemporaryShelters
            .Include(s => s.Provider)
            .FirstOrDefaultAsync(s => s.ShelterId == shelterId, cancellationToken);
    }

    public async Task<IEnumerable<TemporaryShelter>> GetAvailableSheltersInAreaAsync(string geohashPrefix, CancellationToken cancellationToken = default)
    {
        return await _context.TemporaryShelters
            .Where(s => s.LocationGeohash.StartsWith(geohashPrefix) 
                && s.Status == "open"
                && s.CurrentOccupancy < s.Capacity)
            .OrderByDescending(s => s.Capacity - s.CurrentOccupancy)
            .ToListAsync(cancellationToken);
    }

    public async Task<TemporaryShelter> CreateShelterAsync(TemporaryShelter shelter, CancellationToken cancellationToken = default)
    {
        if (shelter.ShelterId == Guid.Empty)
            shelter.ShelterId = Guid.NewGuid();
        shelter.Status = "open";
        shelter.CurrentOccupancy = 0;
        shelter.CreatedAt = DateTime.UtcNow;
        shelter.UpdatedAt = DateTime.UtcNow;

        _context.TemporaryShelters.Add(shelter);
        await _context.SaveChangesAsync(cancellationToken);

        return shelter;
    }

    public async Task UpdateShelterOccupancyAsync(Guid shelterId, int delta, CancellationToken cancellationToken = default)
    {
        var shelter = await _context.TemporaryShelters.FindAsync(new object[] { shelterId }, cancellationToken);
        if (shelter != null)
        {
            shelter.CurrentOccupancy = Math.Max(0, shelter.CurrentOccupancy + delta);
            shelter.UpdatedAt = DateTime.UtcNow;

            // Auto-update status based on capacity
            if (shelter.CurrentOccupancy >= shelter.Capacity)
            {
                shelter.Status = "full";
            }
            else if (shelter.Status == "full")
            {
                shelter.Status = "open";
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateShelterStatusAsync(Guid shelterId, string status, CancellationToken cancellationToken = default)
    {
        var shelter = await _context.TemporaryShelters.FindAsync(new object[] { shelterId }, cancellationToken);
        if (shelter != null)
        {
            shelter.Status = status;
            shelter.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // Active Evacuation operations

    public async Task<ActiveEvacuation?> GetActiveEvacuationAsync(Guid evacuationId, CancellationToken cancellationToken = default)
    {
        return await _context.ActiveEvacuations
            .Include(e => e.Request)
            .Include(e => e.Offer)
            .FirstOrDefaultAsync(e => e.EvacuationId == evacuationId, cancellationToken);
    }

    public async Task<ActiveEvacuation> CreateActiveEvacuationAsync(ActiveEvacuation evacuation, CancellationToken cancellationToken = default)
    {
        if (evacuation.EvacuationId == Guid.Empty)
            evacuation.EvacuationId = Guid.NewGuid();
        evacuation.CreatedAt = DateTime.UtcNow;
        evacuation.UpdatedAt = DateTime.UtcNow;

        _context.ActiveEvacuations.Add(evacuation);
        await _context.SaveChangesAsync(cancellationToken);

        return evacuation;
    }

    public async Task UpdateEvacuationStatusAsync(Guid evacuationId, string status, CancellationToken cancellationToken = default)
    {
        var evacuation = await _context.ActiveEvacuations.FindAsync(new object[] { evacuationId }, cancellationToken);
        if (evacuation != null)
        {
            evacuation.Status = status;
            evacuation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // Evacuation Offer operations

    public async Task<EvacuationResourceOffer?> GetOfferByIdAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationResourceOffers
            .Include(o => o.Provider)
            .FirstOrDefaultAsync(o => o.OfferId == offerId, cancellationToken);
    }

    public async Task<IEnumerable<EvacuationResourceOffer>> GetActiveOffersInAreaAsync(string geohashPrefix, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationResourceOffers
            .Where(o => o.LocationGeohash.StartsWith(geohashPrefix)
                && o.Status == "active"
                && (o.AvailableUntil == null || o.AvailableUntil > DateTime.UtcNow))
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<EvacuationResourceOffer>> GetUserOffersAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationResourceOffers
            .Where(o => o.ProviderId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<EvacuationResourceOffer> CreateOfferAsync(EvacuationResourceOffer offer, CancellationToken cancellationToken = default)
    {
        if (offer.OfferId == Guid.Empty)
            offer.OfferId = Guid.NewGuid();
        offer.Status = "active";
        offer.CurrentMatches = 0;
        offer.CreatedAt = DateTime.UtcNow;
        offer.UpdatedAt = DateTime.UtcNow;

        _context.EvacuationResourceOffers.Add(offer);
        await _context.SaveChangesAsync(cancellationToken);

        return offer;
    }

    public async Task UpdateOfferAsync(EvacuationResourceOffer offer, CancellationToken cancellationToken = default)
    {
        offer.UpdatedAt = DateTime.UtcNow;
        _context.EvacuationResourceOffers.Update(offer);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateOfferStatusAsync(Guid offerId, string status, CancellationToken cancellationToken = default)
    {
        var offer = await _context.EvacuationResourceOffers.FindAsync(new object[] { offerId }, cancellationToken);
        if (offer != null)
        {
            offer.Status = status;
            offer.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // Match Proposal operations

    public async Task<EvacuationMatchProposal?> GetMatchByIdAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationMatchProposals
            .Include(m => m.Request)
            .Include(m => m.Offer)
            .FirstOrDefaultAsync(m => m.MatchId == matchId, cancellationToken);
    }

    public async Task<IEnumerable<EvacuationMatchProposal>> GetMatchesForRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationMatchProposals
            .Where(m => m.RequestId == requestId)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<EvacuationMatchProposal>> GetMatchesForOfferAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationMatchProposals
            .Where(m => m.OfferId == offerId)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<EvacuationMatchProposal> CreateMatchAsync(EvacuationMatchProposal match, CancellationToken cancellationToken = default)
    {
        if (match.MatchId == Guid.Empty)
            match.MatchId = Guid.NewGuid();
        match.Status = "proposed";
        match.CreatedAt = DateTime.UtcNow;

        _context.EvacuationMatchProposals.Add(match);
        await _context.SaveChangesAsync(cancellationToken);

        return match;
    }

    public async Task UpdateMatchStatusAsync(Guid matchId, string status, string? declineReason = null, CancellationToken cancellationToken = default)
    {
        var match = await _context.EvacuationMatchProposals.FindAsync(new object[] { matchId }, cancellationToken);
        if (match != null)
        {
            match.Status = status;
            match.DeclineReason = declineReason;
            match.RespondedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<EvacuationMatchProposal>> GetExpiredMatchesAsync(DateTime cutoffTime, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationMatchProposals
            .Where(m => m.Status == "proposed"
                && m.ExpiresAt.HasValue
                && m.ExpiresAt.Value < cutoffTime)
            .ToListAsync(cancellationToken);
    }

    // Evacuation Location operations

    public async Task<EvacuationLocation> CreateLocationAsync(EvacuationLocation location, CancellationToken cancellationToken = default)
    {
        if (location.LocationId == Guid.Empty)
            location.LocationId = Guid.NewGuid();
        location.Timestamp = DateTime.UtcNow;

        _context.EvacuationLocations.Add(location);
        await _context.SaveChangesAsync(cancellationToken);

        return location;
    }

    public async Task<IEnumerable<EvacuationLocation>> GetEvacuationLocationsAsync(Guid evacuationId, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationLocations
            .Where(l => l.EvacuationId == evacuationId)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync(cancellationToken);
    }

    // Evacuation Message operations

    public async Task<EvacuationMessage> CreateMessageAsync(EvacuationMessage message, CancellationToken cancellationToken = default)
    {
        if (message.MessageId == Guid.Empty)
            message.MessageId = Guid.NewGuid();
        message.Timestamp = DateTime.UtcNow;

        _context.EvacuationMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);

        return message;
    }

    public async Task<IEnumerable<EvacuationMessage>> GetEvacuationMessagesAsync(Guid evacuationId, int limit = 50, CancellationToken cancellationToken = default)
    {
        return await _context.EvacuationMessages
            .Where(m => m.EvacuationId == evacuationId)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    // Shelter Check-in operations

    public async Task<ShelterCheckIn> CreateCheckInAsync(ShelterCheckIn checkIn, CancellationToken cancellationToken = default)
    {
        if (checkIn.CheckInId == Guid.Empty)
            checkIn.CheckInId = Guid.NewGuid();
        checkIn.CheckInTime = DateTime.UtcNow;

        _context.ShelterCheckIns.Add(checkIn);
        await _context.SaveChangesAsync(cancellationToken);

        return checkIn;
    }

    public async Task<IEnumerable<ShelterCheckIn>> GetShelterCheckInsAsync(Guid shelterId, CancellationToken cancellationToken = default)
    {
        return await _context.ShelterCheckIns
            .Where(c => c.ShelterId == shelterId)
            .OrderByDescending(c => c.CheckInTime)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateCheckInStatusAsync(Guid checkInId, string status, CancellationToken cancellationToken = default)
    {
        var checkIn = await _context.ShelterCheckIns.FindAsync(new object[] { checkInId }, cancellationToken);
        if (checkIn != null)
        {
            checkIn.Status = status;
            if (status == "checked_out")
                checkIn.CheckOutTime = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
