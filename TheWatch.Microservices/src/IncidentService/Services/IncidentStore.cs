using System.Collections.Concurrent;
using TheWatch.Microservices.Emergency.IncidentService.Models;

namespace TheWatch.Microservices.Emergency.IncidentService.Services;

public interface IIncidentStore
{
    Task<Incident> CreateAsync(CreateIncidentRequest request);
    Task<Incident?> GetByIdAsync(string id);
    Task<IEnumerable<Incident>> GetAllAsync(IncidentStatus? status = null, IncidentSeverity? severity = null, string? incidentType = null);
    Task<Incident?> UpdateAsync(string id, UpdateIncidentRequest request);
    Task<Incident?> UpdateStatusAsync(string id, UpdateStatusRequest request);
    Task<Incident?> EscalateAsync(string id, EscalateIncidentRequest request);
    Task<bool> DeleteAsync(string id);
    Task<bool> AssignResponderAsync(string id, string responderId, string callsign);
}

public class InMemoryIncidentStore : IIncidentStore
{
    private static readonly ConcurrentDictionary<string, Incident> Incidents = new();

    static InMemoryIncidentStore()
    {
        var sample1 = new Incident
        {
            Id = "INC-1001",
            Title = "Multi-Vehicle Collision on I-95 Southbound",
            Description = "3-vehicle pileup with hazardous fuel spill and trapped occupants.",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Dispatched,
            IncidentType = "MassCasualty",
            Latitude = 37.7749,
            Longitude = -122.4194,
            Address = "Mile Marker 44, I-95 South, Sector 4",
            CallerContact = "+1-555-0199",
            AssignedResponderId = "UNIT-MEDIC-42",
            AssignedResponderCallsign = "MEDIC-42",
            EstimatedCasualties = 4,
            Tags = new List<string> { "Highway", "ExtricationRequired", "FuelLeak" },
            Timeline = new List<IncidentTimelineEntry>
            {
                new() { Action = "DETECTED", PerformedBy = "911 Ingest CAD", Details = "Call received from highway bystander." },
                new() { Action = "DISPATCHED", PerformedBy = "DISPATCH-1", Details = "Dispatched MEDIC-42 and DRONE-9." }
            },
            ReportedAtUtc = DateTime.UtcNow.AddMinutes(-25)
        };

        var sample2 = new Incident
        {
            Id = "INC-1002",
            Title = "Commercial Building Structure Fire",
            Description = "Heavy black smoke billowing from roof of warehouse. Potential civilian inside.",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.OnScene,
            IncidentType = "Fire",
            Latitude = 37.7833,
            Longitude = -122.4167,
            Address = "742 Evergreen Terrace, Industrial Park",
            CallerContact = "+1-555-0211",
            AssignedResponderId = "UNIT-FIRE-07",
            AssignedResponderCallsign = "ENGINE-7",
            EstimatedCasualties = 2,
            Tags = new List<string> { "StructureFire", "HazmatRisk" },
            Timeline = new List<IncidentTimelineEntry>
            {
                new() { Action = "DETECTED", PerformedBy = "IoT Sensor Network", Details = "Thermal anomaly and particulate threshold tripped." },
                new() { Action = "ON_SCENE", PerformedBy = "ENGINE-7", Details = "Water line connected, primary search commenced." }
            },
            ReportedAtUtc = DateTime.UtcNow.AddMinutes(-12)
        };

        var sample3 = new Incident
        {
            Id = "INC-1003",
            Title = "Cardiac Arrest at Downtown Metro Station",
            Description = "Adult male collapsed near turnstiles, bystander CPR in progress, AED deployed.",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.ContainmentInProgress,
            IncidentType = "Medical",
            Latitude = 37.7891,
            Longitude = -122.4014,
            Address = "Market St & 4th St, Metro Concourse",
            CallerContact = "+1-555-0344",
            AssignedResponderId = "UNIT-AED-DRONE-1",
            AssignedResponderCallsign = "AERO-MED-1",
            EstimatedCasualties = 1,
            Tags = new List<string> { "CardiacArrest", "AED_Delivered" },
            Timeline = new List<IncidentTimelineEntry>
            {
                new() { Action = "DETECTED", PerformedBy = "Metro CCTV AI", Details = "Fall detection trigger verified." }
            },
            ReportedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };

        Incidents[sample1.Id] = sample1;
        Incidents[sample2.Id] = sample2;
        Incidents[sample3.Id] = sample3;
    }

    public Task<Incident> CreateAsync(CreateIncidentRequest request)
    {
        var incident = new Incident
        {
            Id = $"INC-{Random.Shared.Next(1004, 9999)}",
            Title = request.Title,
            Description = request.Description,
            Severity = request.Severity,
            Status = IncidentStatus.Detected,
            IncidentType = request.IncidentType,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Address = request.Address,
            CallerContact = request.CallerContact,
            EstimatedCasualties = request.EstimatedCasualties,
            Tags = request.Tags ?? new List<string>(),
            Timeline = new List<IncidentTimelineEntry>
            {
                new()
                {
                    Action = "CREATED",
                    PerformedBy = "CAD Gateway",
                    Details = $"Incident initialized with severity {request.Severity}."
                }
            },
            ReportedAtUtc = DateTime.UtcNow
        };

        Incidents[incident.Id] = incident;
        return Task.FromResult(incident);
    }

    public Task<Incident?> GetByIdAsync(string id)
    {
        Incidents.TryGetValue(id, out var incident);
        return Task.FromResult(incident);
    }

    public Task<IEnumerable<Incident>> GetAllAsync(IncidentStatus? status = null, IncidentSeverity? severity = null, string? incidentType = null)
    {
        IEnumerable<Incident> results = Incidents.Values;

        if (status.HasValue)
            results = results.Where(i => i.Status == status.Value);

        if (severity.HasValue)
            results = results.Where(i => i.Severity == severity.Value);

        if (!string.IsNullOrWhiteSpace(incidentType))
            results = results.Where(i => i.IncidentType.Equals(incidentType, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(results.OrderByDescending(i => i.ReportedAtUtc).AsEnumerable());
    }

    public Task<Incident?> UpdateAsync(string id, UpdateIncidentRequest request)
    {
        if (!Incidents.TryGetValue(id, out var incident))
            return Task.FromResult<Incident?>(null);

        if (!string.IsNullOrWhiteSpace(request.Title)) incident.Title = request.Title;
        if (!string.IsNullOrWhiteSpace(request.Description)) incident.Description = request.Description;
        if (request.Severity.HasValue) incident.Severity = request.Severity.Value;
        if (request.Latitude.HasValue) incident.Latitude = request.Latitude.Value;
        if (request.Longitude.HasValue) incident.Longitude = request.Longitude.Value;
        if (!string.IsNullOrWhiteSpace(request.Address)) incident.Address = request.Address;
        if (request.EstimatedCasualties.HasValue) incident.EstimatedCasualties = request.EstimatedCasualties.Value;
        if (request.Tags != null) incident.Tags = request.Tags;

        incident.Timeline.Add(new IncidentTimelineEntry
        {
            Action = "UPDATED",
            PerformedBy = "Operator",
            Details = "Incident metadata modified."
        });

        return Task.FromResult<Incident?>(incident);
    }

    public Task<Incident?> UpdateStatusAsync(string id, UpdateStatusRequest request)
    {
        if (!Incidents.TryGetValue(id, out var incident))
            return Task.FromResult<Incident?>(null);

        incident.Status = request.Status;
        if (request.Status == IncidentStatus.Resolved || request.Status == IncidentStatus.Closed)
        {
            incident.ResolvedAtUtc = DateTime.UtcNow;
        }

        incident.Timeline.Add(new IncidentTimelineEntry
        {
            Action = $"STATUS_{request.Status.ToString().ToUpperInvariant()}",
            PerformedBy = request.PerformedBy,
            Details = string.IsNullOrWhiteSpace(request.Reason) ? $"Status changed to {request.Status}" : request.Reason
        });

        return Task.FromResult<Incident?>(incident);
    }

    public Task<Incident?> EscalateAsync(string id, EscalateIncidentRequest request)
    {
        if (!Incidents.TryGetValue(id, out var incident))
            return Task.FromResult<Incident?>(null);

        var oldSeverity = incident.Severity;
        incident.Severity = request.TargetSeverity;

        incident.Timeline.Add(new IncidentTimelineEntry
        {
            Action = "ESCALATED",
            PerformedBy = string.IsNullOrWhiteSpace(request.EscalatedBy) ? "Commander" : request.EscalatedBy,
            Details = $"Severity escalated from {oldSeverity} to {request.TargetSeverity}. Justification: {request.Justification}"
        });

        return Task.FromResult<Incident?>(incident);
    }

    public Task<bool> DeleteAsync(string id)
    {
        return Task.FromResult(Incidents.TryRemove(id, out _));
    }

    public Task<bool> AssignResponderAsync(string id, string responderId, string callsign)
    {
        if (!Incidents.TryGetValue(id, out var incident))
            return Task.FromResult(false);

        incident.AssignedResponderId = responderId;
        incident.AssignedResponderCallsign = callsign;
        incident.Status = IncidentStatus.Dispatched;

        incident.Timeline.Add(new IncidentTimelineEntry
        {
            Action = "RESPONDER_ASSIGNED",
            PerformedBy = "Dispatch Engine",
            Details = $"Assigned responder {callsign} ({responderId})"
        });

        return Task.FromResult(true);
    }
}
