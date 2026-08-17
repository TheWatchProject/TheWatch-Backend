namespace TheWatch.Microservices.Dispatch.DispatchService.Models;

public enum UnitType
{
    Ambulance,
    ParamedicQuickResponse,
    FireEngine,
    HazMatUnit,
    AutonomousAedDrone,
    SurveillanceDrone,
    RescueHelicopter,
    PolicePatrol
}

public enum UnitReadiness
{
    Available,
    Dispatched,
    EnRoute,
    OnScene,
    Transporting,
    ReturningToStation,
    Maintenance,
    Offline
}

public class ResponderUnit
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Callsign { get; set; } = string.Empty;
    public UnitType Type { get; set; } = UnitType.Ambulance;
    public UnitReadiness Status { get; set; } = UnitReadiness.Available;
    public string CurrentIncidentId { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int BatteryOrFuelPercent { get; set; } = 100;
    public List<string> Capabilities { get; set; } = new();
    public DateTime LastStatusUpdateUtc { get; set; } = DateTime.UtcNow;
}

public class DispatchAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IncidentId { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string UnitCallsign { get; set; } = string.Empty;
    public UnitType UnitType { get; set; }
    public UnitReadiness Status { get; set; } = UnitReadiness.Dispatched;
    public double EstimatedArrivalMinutes { get; set; }
    public string DispatchedBy { get; set; } = "DispatchEngine";
    public string PriorityNotes { get; set; } = string.Empty;
    public DateTime DispatchedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ArrivedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public class DispatchRecommendationRequest
{
    public string IncidentId { get; set; } = string.Empty;
    public string IncidentType { get; set; } = "Medical";
    public double IncidentLatitude { get; set; }
    public double IncidentLongitude { get; set; }
    public int MaxRecommendations { get; set; } = 3;
}

public class UnitRecommendation
{
    public ResponderUnit Unit { get; set; } = new();
    public double DistanceKm { get; set; }
    public double EstimatedEtaMinutes { get; set; }
    public double MatchScore { get; set; }
    public string RecommendationReason { get; set; } = string.Empty;
}

public class DispatchRecommendationResponse
{
    public string IncidentId { get; set; } = string.Empty;
    public List<UnitRecommendation> RecommendedUnits { get; set; } = new();
}

public class AssignUnitRequest
{
    public string IncidentId { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string DispatchedBy { get; set; } = "Operator";
    public string PriorityNotes { get; set; } = string.Empty;
}

public class UpdateDispatchStatusRequest
{
    public UnitReadiness NewStatus { get; set; }
    public string Notes { get; set; } = string.Empty;
    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
}
