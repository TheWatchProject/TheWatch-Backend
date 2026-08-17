namespace TheWatch.Microservices.Medical.TriageService.Models;

public enum TriageCategory
{
    Immediate, // Red: Life-threatening, immediate intervention required
    Delayed,   // Yellow: Serious injury, transport can be delayed
    Minor,     // Green: Walking wounded
    Expectant  // Black: Deceased or non-survivable injuries
}

public class VitalSigns
{
    public double HeartRateBpm { get; set; }
    public double RespiratoryRateBpm { get; set; }
    public double SystolicBp { get; set; }
    public double DiastolicBp { get; set; }
    public double OxygenSaturationSpO2 { get; set; }
    public double BodyTemperatureCelsius { get; set; }
    public int GlasgowComaScale { get; set; } = 15; // 3 to 15
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public class TriageAssessment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IncidentId { get; set; } = string.Empty;
    public string CasualtyIdentifier { get; set; } = string.Empty; // e.g. "CAS-01" or patient name
    public string AssessedByCallsign { get; set; } = string.Empty;
    public TriageCategory Category { get; set; } = TriageCategory.Immediate;
    public string PrimaryInjury { get; set; } = string.Empty;
    public bool CanWalk { get; set; }
    public bool HasSpontaneousBreathing { get; set; } = true;
    public bool RadialPulsePresent { get; set; } = true;
    public bool FollowsCommands { get; set; } = true;
    public VitalSigns Vitals { get; set; } = new();
    public List<string> ClinicalNotes { get; set; } = new();
    public DateTime AssessedAtUtc { get; set; } = DateTime.UtcNow;
}

public class StartTriageAssessmentRequest
{
    public string IncidentId { get; set; } = string.Empty;
    public string CasualtyIdentifier { get; set; } = string.Empty;
    public string AssessedByCallsign { get; set; } = string.Empty;
    public string PrimaryInjury { get; set; } = string.Empty;
    public bool CanWalk { get; set; }
    public bool HasSpontaneousBreathing { get; set; }
    public double RespiratoryRateBpm { get; set; }
    public bool RadialPulsePresent { get; set; }
    public double CapillaryRefillSeconds { get; set; } = 2.0;
    public bool FollowsCommands { get; set; }
    public VitalSigns? InitialVitals { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class RecordVitalsRequest
{
    public string AssessmentId { get; set; } = string.Empty;
    public VitalSigns Vitals { get; set; } = new();
}

public class IncidentTriageSummary
{
    public string IncidentId { get; set; } = string.Empty;
    public int TotalCasualtiesAssessed { get; set; }
    public int ImmediateRedCount { get; set; }
    public int DelayedYellowCount { get; set; }
    public int MinorGreenCount { get; set; }
    public int ExpectantBlackCount { get; set; }
    public List<TriageAssessment> Assessments { get; set; } = new();
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
