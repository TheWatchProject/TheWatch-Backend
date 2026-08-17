using Microsoft.Extensions.Logging;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service for managing shelter capacity tracking and availability.
/// </summary>
public class ShelterCapacityManager : IShelterCapacityManager
{
    private readonly IEvacuationRepository _evacuationRepository;
    private readonly GeohashService _geohashService;
    private readonly ILogger<ShelterCapacityManager> _logger;

    public ShelterCapacityManager(
        IEvacuationRepository evacuationRepository,
        ILogger<ShelterCapacityManager> logger)
    {
        _evacuationRepository = evacuationRepository;
        _geohashService = new GeohashService();
        _logger = logger;
    }

    public async Task<bool> CheckInAsync(
        Guid shelterId,
        int partySize,
        Guid evacueeId,
        CancellationToken cancellationToken = default)
    {
        var shelter = await _evacuationRepository.GetShelterByIdAsync(shelterId, cancellationToken);
        if (shelter == null)
        {
            _logger.LogWarning("Shelter {ShelterId} not found", shelterId);
            return false;
        }

        // Check capacity
        if (shelter.CurrentOccupancy + partySize > shelter.Capacity)
        {
            _logger.LogWarning(
                "Shelter {ShelterId} at capacity: {Current}/{Total}, cannot accommodate {PartySize}",
                shelterId, shelter.CurrentOccupancy, shelter.Capacity, partySize);
            return false;
        }

        // Create check-in record
        var checkIn = new ShelterCheckIn
        {
            CheckInId = Guid.NewGuid(),
            ShelterId = shelterId,
            EvacueeId = evacueeId,
            PartySize = partySize,
            Status = "checked_in",
            CheckInTime = DateTime.UtcNow
        };

        await _evacuationRepository.CreateCheckInAsync(checkIn, cancellationToken);

        // Update shelter occupancy
        await _evacuationRepository.UpdateShelterOccupancyAsync(shelterId, partySize, cancellationToken);

        _logger.LogInformation(
            "Evacuee {EvacueeId} checked in to shelter {ShelterId} with party size {PartySize}",
            evacueeId, shelterId, partySize);

        return true;
    }

    public async Task<bool> CheckOutAsync(
        Guid shelterId,
        int partySize,
        Guid evacueeId,
        CancellationToken cancellationToken = default)
    {
        var shelter = await _evacuationRepository.GetShelterByIdAsync(shelterId, cancellationToken);
        if (shelter == null)
        {
            _logger.LogWarning("Shelter {ShelterId} not found", shelterId);
            return false;
        }

        // Find active check-in for evacuee
        var checkIns = await _evacuationRepository.GetShelterCheckInsAsync(shelterId, cancellationToken);
        var activeCheckIn = checkIns.FirstOrDefault(c =>
            c.EvacueeId == evacueeId && c.Status == "checked_in");

        if (activeCheckIn == null)
        {
            _logger.LogWarning(
                "No active check-in found for evacuee {EvacueeId} at shelter {ShelterId}",
                evacueeId, shelterId);
            return false;
        }

        // Update check-in status
        await _evacuationRepository.UpdateCheckInStatusAsync(activeCheckIn.CheckInId, "checked_out", cancellationToken);

        // Update shelter occupancy (decrease)
        await _evacuationRepository.UpdateShelterOccupancyAsync(shelterId, -partySize, cancellationToken);

        _logger.LogInformation(
            "Evacuee {EvacueeId} checked out from shelter {ShelterId} with party size {PartySize}",
            evacueeId, shelterId, partySize);

        return true;
    }

    public async Task<IEnumerable<TemporaryShelter>> FindAvailableSheltersAsync(
        double latitude,
        double longitude,
        int radiusKm = 25,
        int requiredCapacity = 1,
        IEnumerable<string>? specialNeeds = null,
        CancellationToken cancellationToken = default)
    {
        var geohash = _geohashService.Encode(latitude, longitude, 5); // Precision 5 = ~2.4km
        var searchPrefix = geohash[..Math.Min(4, geohash.Length)]; // Widen search for larger radius

        var shelters = await _evacuationRepository.GetAvailableSheltersInAreaAsync(searchPrefix, cancellationToken);

        // Also check neighboring geohashes
        var neighbors = _geohashService.GetNeighbors(searchPrefix);
        foreach (var neighbor in neighbors)
        {
            var neighborShelters = await _evacuationRepository.GetAvailableSheltersInAreaAsync(neighbor[..4], cancellationToken);
            shelters = shelters.Concat(neighborShelters);
        }

        // Filter by distance and capacity
        var filtered = shelters
            .DistinctBy(s => s.ShelterId)
            .Select(s => new
            {
                Shelter = s,
                Distance = _geohashService.CalculateDistance(latitude, longitude, s.LocationLat, s.LocationLng)
            })
            .Where(x => x.Distance <= radiusKm * 1000) // Convert km to meters
            .Where(x => x.Shelter.Capacity - x.Shelter.CurrentOccupancy >= requiredCapacity)
            .OrderBy(x => x.Distance)
            .Select(x => x.Shelter);

        // Filter by special needs if specified
        if (specialNeeds != null && specialNeeds.Any())
        {
            var needsList = specialNeeds.ToList();
            filtered = filtered.Where(s => CanAccommodateSpecialNeedsSync(s, needsList));
        }

        return filtered;
    }

    public async Task<ShelterOccupancy> GetOccupancyAsync(
        Guid shelterId,
        CancellationToken cancellationToken = default)
    {
        var shelter = await _evacuationRepository.GetShelterByIdAsync(shelterId, cancellationToken);
        if (shelter == null)
        {
            throw new InvalidOperationException($"Shelter {shelterId} not found");
        }

        return new ShelterOccupancy
        {
            ShelterId = shelterId,
            TotalCapacity = shelter.Capacity,
            CurrentOccupancy = shelter.CurrentOccupancy,
            Status = shelter.Status
        };
    }

    public async Task UpdateCapacityAsync(
        Guid shelterId,
        int newCapacity,
        CancellationToken cancellationToken = default)
    {
        var shelter = await _evacuationRepository.GetShelterByIdAsync(shelterId, cancellationToken);
        if (shelter == null)
        {
            throw new InvalidOperationException($"Shelter {shelterId} not found");
        }

        if (newCapacity < shelter.CurrentOccupancy)
        {
            throw new InvalidOperationException(
                $"Cannot reduce capacity below current occupancy ({shelter.CurrentOccupancy})");
        }

        shelter.Capacity = newCapacity;
        shelter.UpdatedAt = DateTime.UtcNow;

        // Update status if capacity change affects it
        if (shelter.CurrentOccupancy >= newCapacity)
        {
            shelter.Status = "full";
        }
        else if (shelter.Status == "full")
        {
            shelter.Status = "open";
        }

        await _evacuationRepository.UpdateShelterStatusAsync(shelterId, shelter.Status, cancellationToken);

        _logger.LogInformation(
            "Updated shelter {ShelterId} capacity from {OldCapacity} to {NewCapacity}",
            shelterId, shelter.Capacity, newCapacity);
    }

    public async Task<bool> CanAccommodateSpecialNeedsAsync(
        Guid shelterId,
        IEnumerable<string> specialNeeds,
        CancellationToken cancellationToken = default)
    {
        var shelter = await _evacuationRepository.GetShelterByIdAsync(shelterId, cancellationToken);
        if (shelter == null)
        {
            return false;
        }

        return CanAccommodateSpecialNeedsSync(shelter, specialNeeds.ToList());
    }

    // Private helper method
    private bool CanAccommodateSpecialNeedsSync(TemporaryShelter shelter, List<string> specialNeeds)
    {
        foreach (var need in specialNeeds)
        {
            switch (need.ToLowerInvariant())
            {
                case "wheelchair":
                case "wheelchair_accessible":
                    if (!shelter.WheelchairAccessible)
                        return false;
                    break;

                case "pets":
                case "pet":
                    if (!shelter.AcceptsPets)
                        return false;
                    break;

                case "medical":
                case "oxygen":
                case "medical_equipment":
                    if (string.IsNullOrEmpty(shelter.MedicalFacilities) || shelter.MedicalFacilities == "[]")
                        return false;
                    break;

                default:
                    // Check amenities for other needs
                    if (!string.IsNullOrEmpty(shelter.Amenities))
                    {
                        try
                        {
                            var amenities = JsonSerializer.Deserialize<List<string>>(shelter.Amenities);
                            if (amenities == null || !amenities.Contains(need, StringComparer.OrdinalIgnoreCase))
                                return false;
                        }
                        catch
                        {
                            // If can't parse amenities, assume need cannot be met
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                    break;
            }
        }

        return true; // All needs can be accommodated
    }
}
