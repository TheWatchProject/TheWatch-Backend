using System.Collections.Concurrent;
using TheWatch.Microservices.Dispatch.DispatchService.Models;

namespace TheWatch.Microservices.Dispatch.DispatchService.Services;

public interface IDispatchStore
{
    Task<IEnumerable<ResponderUnit>> GetAllUnitsAsync(UnitReadiness? status = null, UnitType? type = null);
    Task<ResponderUnit?> GetUnitByIdAsync(string id);
    Task<DispatchRecommendationResponse> RecommendUnitsAsync(DispatchRecommendationRequest request);
    Task<DispatchAssignment> AssignUnitAsync(AssignUnitRequest request);
    Task<DispatchAssignment?> UpdateAssignmentStatusAsync(string assignmentId, UpdateDispatchStatusRequest request);
    Task<IEnumerable<DispatchAssignment>> GetAssignmentsAsync(string? incidentId = null, string? unitId = null, bool activeOnly = false);
    Task<DispatchAssignment?> GetAssignmentByIdAsync(string id);
    Task<bool> ReleaseUnitAsync(string assignmentId);
}

public class InMemoryDispatchStore : IDispatchStore
{
    private static readonly ConcurrentDictionary<string, ResponderUnit> Units = new();
    private static readonly ConcurrentDictionary<string, DispatchAssignment> Assignments = new();

    static InMemoryDispatchStore()
    {
        var u1 = new ResponderUnit
        {
            Id = "UNIT-MEDIC-42",
            Callsign = "MEDIC-42 (Advanced ALS)",
            Type = UnitType.Ambulance,
            Status = UnitReadiness.Available,
            Latitude = 37.7750,
            Longitude = -122.4180,
            BatteryOrFuelPercent = 95,
            Capabilities = new List<string> { "ALS", "Ventilator", "Defibrillator", "BloodSupply" }
        };

        var u2 = new ResponderUnit
        {
            Id = "UNIT-FIRE-07",
            Callsign = "ENGINE-7 (Heavy Rescue)",
            Type = UnitType.FireEngine,
            Status = UnitReadiness.Available,
            Latitude = 37.7840,
            Longitude = -122.4150,
            BatteryOrFuelPercent = 90,
            Capabilities = new List<string> { "ExtricationJaws", "WaterTank2000L", "ThermalImager" }
        };

        var u3 = new ResponderUnit
        {
            Id = "UNIT-AED-DRONE-1",
            Callsign = "AERO-MED-1 (Autonomous First Responder)",
            Type = UnitType.AutonomousAedDrone,
            Status = UnitReadiness.Available,
            Latitude = 37.7885,
            Longitude = -122.4020,
            BatteryOrFuelPercent = 88,
            Capabilities = new List<string> { "AEDPayload", "EpiPen", "RealtimeVideoLink", "NightVision" }
        };

        var u4 = new ResponderUnit
        {
            Id = "UNIT-HELO-3",
            Callsign = "AIR-MEDEVAC-3",
            Type = UnitType.RescueHelicopter,
            Status = UnitReadiness.Available,
            Latitude = 37.7600,
            Longitude = -122.3900,
            BatteryOrFuelPercent = 80,
            Capabilities = new List<string> { "CriticalCareTransport", "HoistSystem", "TraumaLevel1" }
        };

        Units[u1.Id] = u1;
        Units[u2.Id] = u2;
        Units[u3.Id] = u3;
        Units[u4.Id] = u4;
    }

    public Task<IEnumerable<ResponderUnit>> GetAllUnitsAsync(UnitReadiness? status = null, UnitType? type = null)
    {
        IEnumerable<ResponderUnit> list = Units.Values;
        if (status.HasValue) list = list.Where(u => u.Status == status.Value);
        if (type.HasValue) list = list.Where(u => u.Type == type.Value);
        return Task.FromResult(list);
    }

    public Task<ResponderUnit?> GetUnitByIdAsync(string id)
    {
        Units.TryGetValue(id, out var unit);
        return Task.FromResult(unit);
    }

    public Task<DispatchRecommendationResponse> RecommendUnitsAsync(DispatchRecommendationRequest request)
    {
        var candidates = Units.Values.Where(u => u.Status == UnitReadiness.Available).ToList();

        var ranked = candidates.Select(unit =>
        {
            var distanceKm = CalculateHaversineDistanceKm(request.IncidentLatitude, request.IncidentLongitude, unit.Latitude, unit.Longitude);
            var speedKmh = unit.Type == UnitType.AutonomousAedDrone ? 80.0 : unit.Type == UnitType.RescueHelicopter ? 220.0 : 45.0;
            var etaMinutes = Math.Round((distanceKm / speedKmh) * 60.0 + 1.5, 1); // ETA + dispatch reaction time

            double score = 100.0 - (distanceKm * 5.0) + (unit.BatteryOrFuelPercent * 0.1);
            if (request.IncidentType.Equals("CardiacArrest", StringComparison.OrdinalIgnoreCase) && unit.Type == UnitType.AutonomousAedDrone)
            {
                score += 30.0;
            }
            else if (request.IncidentType.Equals("Fire", StringComparison.OrdinalIgnoreCase) && unit.Type == UnitType.FireEngine)
            {
                score += 40.0;
            }

            var reason = $"Proximity {distanceKm:F2}km (~{etaMinutes} min ETA) with {unit.BatteryOrFuelPercent}% readiness.";

            return new UnitRecommendation
            {
                Unit = unit,
                DistanceKm = Math.Round(distanceKm, 2),
                EstimatedEtaMinutes = etaMinutes,
                MatchScore = Math.Round(score, 1),
                RecommendationReason = reason
            };
        })
        .OrderByDescending(r => r.MatchScore)
        .Take(request.MaxRecommendations)
        .ToList();

        return Task.FromResult(new DispatchRecommendationResponse
        {
            IncidentId = request.IncidentId,
            RecommendedUnits = ranked
        });
    }

    public Task<DispatchAssignment> AssignUnitAsync(AssignUnitRequest request)
    {
        if (!Units.TryGetValue(request.UnitId, out var unit))
        {
            throw new ArgumentException($"Responder unit {request.UnitId} does not exist.");
        }

        unit.Status = UnitReadiness.Dispatched;
        unit.CurrentIncidentId = request.IncidentId;
        unit.LastStatusUpdateUtc = DateTime.UtcNow;

        var assignment = new DispatchAssignment
        {
            Id = $"DISP-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            IncidentId = request.IncidentId,
            UnitId = unit.Id,
            UnitCallsign = unit.Callsign,
            UnitType = unit.Type,
            Status = UnitReadiness.Dispatched,
            EstimatedArrivalMinutes = 4.5,
            DispatchedBy = request.DispatchedBy,
            PriorityNotes = request.PriorityNotes,
            DispatchedAtUtc = DateTime.UtcNow
        };

        Assignments[assignment.Id] = assignment;
        return Task.FromResult(assignment);
    }

    public Task<DispatchAssignment?> UpdateAssignmentStatusAsync(string assignmentId, UpdateDispatchStatusRequest request)
    {
        if (!Assignments.TryGetValue(assignmentId, out var assignment))
            return Task.FromResult<DispatchAssignment?>(null);

        assignment.Status = request.NewStatus;
        if (request.NewStatus == UnitReadiness.OnScene)
        {
            assignment.ArrivedAtUtc = DateTime.UtcNow;
        }
        else if (request.NewStatus == UnitReadiness.Available || request.NewStatus == UnitReadiness.ReturningToStation)
        {
            assignment.CompletedAtUtc = DateTime.UtcNow;
        }

        if (Units.TryGetValue(assignment.UnitId, out var unit))
        {
            unit.Status = request.NewStatus;
            unit.LastStatusUpdateUtc = DateTime.UtcNow;
            if (request.CurrentLatitude.HasValue) unit.Latitude = request.CurrentLatitude.Value;
            if (request.CurrentLongitude.HasValue) unit.Longitude = request.CurrentLongitude.Value;
            if (request.NewStatus == UnitReadiness.Available) unit.CurrentIncidentId = string.Empty;
        }

        return Task.FromResult<DispatchAssignment?>(assignment);
    }

    public Task<IEnumerable<DispatchAssignment>> GetAssignmentsAsync(string? incidentId = null, string? unitId = null, bool activeOnly = false)
    {
        IEnumerable<DispatchAssignment> list = Assignments.Values;

        if (!string.IsNullOrWhiteSpace(incidentId))
            list = list.Where(a => a.IncidentId.Equals(incidentId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(unitId))
            list = list.Where(a => a.UnitId.Equals(unitId, StringComparison.OrdinalIgnoreCase));

        if (activeOnly)
            list = list.Where(a => a.Status != UnitReadiness.Available && a.Status != UnitReadiness.Offline);

        return Task.FromResult(list.OrderByDescending(a => a.DispatchedAtUtc).AsEnumerable());
    }

    public Task<DispatchAssignment?> GetAssignmentByIdAsync(string id)
    {
        Assignments.TryGetValue(id, out var assignment);
        return Task.FromResult(assignment);
    }

    public Task<bool> ReleaseUnitAsync(string assignmentId)
    {
        if (!Assignments.TryGetValue(assignmentId, out var assignment))
            return Task.FromResult(false);

        assignment.Status = UnitReadiness.Available;
        assignment.CompletedAtUtc = DateTime.UtcNow;

        if (Units.TryGetValue(assignment.UnitId, out var unit))
        {
            unit.Status = UnitReadiness.Available;
            unit.CurrentIncidentId = string.Empty;
            unit.LastStatusUpdateUtc = DateTime.UtcNow;
        }

        return Task.FromResult(true);
    }

    private static double CalculateHaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusKm = 6371.0;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
