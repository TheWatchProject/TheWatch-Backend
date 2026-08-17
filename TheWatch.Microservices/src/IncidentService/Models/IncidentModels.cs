namespace TheWatch.Microservices.Emergency.IncidentService.Models;

public enum IncidentSeverity
{
    Low,
    Moderate,
    High,
    Critical,
    Disaster
}

public enum IncidentStatus
{
    Detected,
    TriagePending,
    Dispatched,
    OnScene,
    ContainmentInProgress,
    Resolved,
    Closed,
    Cancelled
}

public class Incident
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.High;
    public IncidentStatus Status { get; set; } = IncidentStatus.Detected;
    public string IncidentType { get; set; } = "Medical"; // Medical, Fire, HazMat, SearchAndRescue, MassCasualty
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Address { get; set; } = string.Empty;
    public string CallerContact { get; set; } = string.Empty;
    public string? AssignedResponderId { get; set; }
    public string? AssignedResponderCallsign { get; set; }
    public int EstimatedCasualties { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<IncidentTimelineEntry> Timeline { get; set; } = new();
    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}

public class IncidentTimelineEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Action { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class CreateIncidentRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.High;
    public string IncidentType { get; set; } = "Medical";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Address { get; set; } = string.Empty;
    public string CallerContact { get; set; } = string.Empty;
    public int EstimatedCasualties { get; set; }
    public List<string>? Tags { get; set; }
}

public class UpdateIncidentRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public IncidentSeverity? Severity { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }
    public int? EstimatedCasualties { get; set; }
    public List<string>? Tags { get; set; }
}

public class UpdateStatusRequest
{
    public IncidentStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = "System";
}

public class EscalateIncidentRequest
{
    public IncidentSeverity TargetSeverity { get; set; }
    public string Justification { get; set; } = string.Empty;
    public string EscalatedBy { get; set; } = string.Empty;
}
