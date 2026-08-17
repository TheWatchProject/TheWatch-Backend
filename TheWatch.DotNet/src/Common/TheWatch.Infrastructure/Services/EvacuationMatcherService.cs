using Microsoft.Extensions.Logging;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service for matching evacuation requests with resource offers.
/// Uses geohash-based proximity + special needs compatibility scoring.
/// </summary>
public class EvacuationMatcherService : IEvacuationMatcherService
{
    private readonly IEvacuationRepository _evacuationRepository;
    private readonly GeohashService _geohashService;
    private readonly ILogger<EvacuationMatcherService> _logger;

    public EvacuationMatcherService(
        IEvacuationRepository evacuationRepository,
        ILogger<EvacuationMatcherService> logger)
    {
        _evacuationRepository = evacuationRepository;
        _geohashService = new GeohashService();
        _logger = logger;
    }

    public async Task<IEnumerable<EvacuationMatchProposal>> FindMatchesForRequestAsync(
        Guid requestId,
        int maxMatches = 5,
        CancellationToken cancellationToken = default)
    {
        var request = await _evacuationRepository.GetRequestByIdAsync(requestId, cancellationToken);
        if (request == null)
        {
            _logger.LogWarning("Evacuation request {RequestId} not found", requestId);
            return Enumerable.Empty<EvacuationMatchProposal>();
        }

        // Get geohash prefix for proximity search (precision 5 = ~2.4km)
        var searchPrefix = request.CurrentLocationGeohash[..5];

        // Get all active offers in the area
        var offers = await _evacuationRepository.GetActiveOffersInAreaAsync(searchPrefix, cancellationToken);

        // Also check neighboring geohashes for edge cases
        var neighbors = _geohashService.GetNeighbors(searchPrefix);
        foreach (var neighbor in neighbors.Take(8)) // All 8 neighbors
        {
            var neighborOffers = await _evacuationRepository.GetActiveOffersInAreaAsync(neighbor[..5], cancellationToken);
            offers = offers.Concat(neighborOffers);
        }

        // Score each offer
        var scoredMatches = new List<(EvacuationResourceOffer offer, double score, double distance)>();

        foreach (var offer in offers.DistinctBy(o => o.OfferId))
        {
            // Check if offer has capacity
            if (offer.CurrentMatches >= offer.MaxMatches)
                continue;

            // Calculate distance
            var distance = _geohashService.CalculateDistance(
                request.CurrentLocationLat,
                request.CurrentLocationLng,
                offer.LocationLat,
                offer.LocationLng);

            // Skip if beyond service radius
            if (distance > offer.ServiceRadiusKm * 1000)
                continue;

            // Calculate match score
            var score = await CalculateMatchScoreAsync(request, offer, cancellationToken);

            if (score > 0)
            {
                scoredMatches.Add((offer, score, distance));
            }
        }

        // Take top matches
        var topMatches = scoredMatches
            .OrderByDescending(m => m.score)
            .ThenBy(m => m.distance)
            .Take(maxMatches);

        // Create match proposals
        var proposals = new List<EvacuationMatchProposal>();
        foreach (var (offer, score, distance) in topMatches)
        {
            var proposal = new EvacuationMatchProposal
            {
                MatchId = Guid.NewGuid(),
                RequestId = requestId,
                OfferId = offer.OfferId,
                Score = score,
                DistanceMeters = distance,
                Status = "proposed",
                ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                CreatedAt = DateTime.UtcNow
            };

            await _evacuationRepository.CreateMatchAsync(proposal, cancellationToken);
            proposals.Add(proposal);
        }

        _logger.LogInformation(
            "Created {Count} match proposals for evacuation request {RequestId}",
            proposals.Count, requestId);

        return proposals;
    }

    public async Task<double> CalculateMatchScoreAsync(
        EvacuationRequest request,
        EvacuationResourceOffer offer,
        CancellationToken cancellationToken = default)
    {
        double score = 0;

        // 1. Resource type match (0-30 points)
        if (request.PreferredResourceType == "any" || request.PreferredResourceType == offer.ResourceType)
        {
            score += 30;
        }
        else if (offer.ResourceType == "any")
        {
            score += 15; // Partial match
        }

        // 2. Proximity score (0-30 points)
        var distance = _geohashService.CalculateDistance(
            request.CurrentLocationLat,
            request.CurrentLocationLng,
            offer.LocationLat,
            offer.LocationLng);

        var proximityScore = CalculateProximityScore(distance, offer.ServiceRadiusKm * 1000);
        score += proximityScore;

        // 3. Special needs compatibility (0-20 points)
        var specialNeedsScore = await CalculateSpecialNeedsScoreAsync(request, offer);
        score += specialNeedsScore;

        // 4. Urgency matching (0-10 points)
        var urgencyScore = CalculateUrgencyScore(request.Urgency);
        score += urgencyScore;

        // 5. Capacity match (0-10 points)
        var capacityScore = CalculateCapacityScore(request.PartySize, offer);
        score += capacityScore;

        return Math.Min(score, 100); // Cap at 100
    }

    public async Task<ActiveEvacuation> AcceptMatchAsync(
        Guid matchId,
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var match = await _evacuationRepository.GetMatchByIdAsync(matchId, cancellationToken);
        if (match == null)
        {
            throw new InvalidOperationException($"Match {matchId} not found");
        }

        if (match.Offer.ProviderId != providerId)
        {
            throw new UnauthorizedAccessException("Provider does not own this offer");
        }

        if (match.Status != "proposed")
        {
            throw new InvalidOperationException($"Match is already {match.Status}");
        }

        // Update match status
        await _evacuationRepository.UpdateMatchStatusAsync(matchId, "accepted", null, cancellationToken);

        // Update request and offer status
        await _evacuationRepository.UpdateRequestStatusAsync(match.RequestId, "matched", cancellationToken);
        await _evacuationRepository.UpdateOfferStatusAsync(match.OfferId, "matched", cancellationToken);

        // Create active evacuation
        var evacuation = new ActiveEvacuation
        {
            EvacuationId = Guid.NewGuid(),
            RequestId = match.RequestId,
            OfferId = match.OfferId,
            EvacueeId = match.Request.EvacueeId,
            ProviderId = providerId,
            ResourceType = match.Offer.ResourceType,
            Status = "meeting_arranged",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _evacuationRepository.CreateActiveEvacuationAsync(evacuation, cancellationToken);

        _logger.LogInformation(
            "Match {MatchId} accepted, created active evacuation {EvacuationId}",
            matchId, evacuation.EvacuationId);

        return evacuation;
    }

    public async Task DeclineMatchAsync(
        Guid matchId,
        Guid providerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var match = await _evacuationRepository.GetMatchByIdAsync(matchId, cancellationToken);
        if (match == null)
        {
            throw new InvalidOperationException($"Match {matchId} not found");
        }

        if (match.Offer.ProviderId != providerId)
        {
            throw new UnauthorizedAccessException("Provider does not own this offer");
        }

        if (match.Status != "proposed")
        {
            throw new InvalidOperationException($"Match is already {match.Status}");
        }

        await _evacuationRepository.UpdateMatchStatusAsync(matchId, "declined", reason, cancellationToken);

        _logger.LogInformation(
            "Match {MatchId} declined by provider {ProviderId}, reason: {Reason}",
            matchId, providerId, reason);
    }

    public async Task<int> ExpireOldMatchesAsync(
        int expirationMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow;
        var expiredMatches = await _evacuationRepository.GetExpiredMatchesAsync(cutoffTime, cancellationToken);

        var count = 0;
        foreach (var match in expiredMatches)
        {
            await _evacuationRepository.UpdateMatchStatusAsync(match.MatchId, "expired", "Match proposal expired", cancellationToken);
            count++;
        }

        if (count > 0)
        {
            _logger.LogInformation("Expired {Count} old match proposals", count);
        }

        return count;
    }

    // Private helper methods

    private double CalculateProximityScore(double distanceMeters, double maxRadiusMeters)
    {
        // Closer is better: linear decay from 30 points at 0km to 0 points at maxRadius
        if (distanceMeters >= maxRadiusMeters)
            return 0;

        var ratio = 1.0 - (distanceMeters / maxRadiusMeters);
        return ratio * 30;
    }

    private async Task<double> CalculateSpecialNeedsScoreAsync(
        EvacuationRequest request,
        EvacuationResourceOffer offer)
    {
        await Task.CompletedTask; // Placeholder for async operations

        if (string.IsNullOrEmpty(request.SpecialNeeds))
            return 20; // No special needs = full score

        var requestNeeds = JsonSerializer.Deserialize<List<string>>(request.SpecialNeeds) ?? new List<string>();
        if (requestNeeds.Count == 0)
            return 20;

        // Check compatibility based on resource type
        if (offer.ResourceType == "shelter" && !string.IsNullOrEmpty(offer.ShelterDetails))
        {
            var shelterDetails = JsonSerializer.Deserialize<Dictionary<string, object>>(offer.ShelterDetails);

            // Check wheelchair accessibility
            if (requestNeeds.Contains("wheelchair"))
            {
                if (shelterDetails?.ContainsKey("wheelchair_accessible") == true &&
                    shelterDetails["wheelchair_accessible"].ToString() == "true")
                {
                    return 20;
                }
                return 0; // Cannot accommodate wheelchair
            }
        }

        // Check pet compatibility
        if (request.HasPets && offer.ResourceType == "vehicle")
        {
            var vehicleDetails = string.IsNullOrEmpty(offer.VehicleDetails)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, object>>(offer.VehicleDetails);

            if (vehicleDetails?.ContainsKey("accepts_pets") == true &&
                vehicleDetails["accepts_pets"].ToString() == "true")
            {
                return 15;
            }
            return 5; // Pets but vehicle may not accommodate
        }

        return 10; // Partial compatibility
    }

    private double CalculateUrgencyScore(string urgency)
    {
        return urgency switch
        {
            "immediate" => 10,
            "within_hours" => 7,
            "within_day" => 5,
            "flexible" => 3,
            _ => 0
        };
    }

    private double CalculateCapacityScore(int partySize, EvacuationResourceOffer offer)
    {
        // Check if offer can handle party size
        if (offer.ResourceType == "vehicle" && !string.IsNullOrEmpty(offer.VehicleDetails))
        {
            var vehicleDetails = JsonSerializer.Deserialize<Dictionary<string, object>>(offer.VehicleDetails);
            if (vehicleDetails?.ContainsKey("capacity") == true)
            {
                if (int.TryParse(vehicleDetails["capacity"].ToString(), out var capacity))
                {
                    if (capacity >= partySize)
                        return 10;
                    return 0; // Insufficient capacity
                }
            }
        }

        // Default scoring for other resource types
        return partySize <= 4 ? 10 : 5;
    }
}
