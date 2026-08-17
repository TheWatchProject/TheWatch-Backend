using System.Collections.Concurrent;
using TheWatch.Microservices.Medical.TriageService.Models;

namespace TheWatch.Microservices.Medical.TriageService.Services;

public interface ITriageEngine
{
    Task<TriageAssessment> AssessCasualtyAsync(StartTriageAssessmentRequest request);
    Task<TriageAssessment?> GetAssessmentByIdAsync(string id);
    Task<IEnumerable<TriageAssessment>> GetAssessmentsByIncidentAsync(string incidentId);
    Task<TriageAssessment?> RecordVitalsAsync(RecordVitalsRequest request);
    Task<IncidentTriageSummary> GetIncidentSummaryAsync(string incidentId);
}

public class InMemoryTriageEngine : ITriageEngine
{
    private static readonly ConcurrentDictionary<string, TriageAssessment> Assessments = new();

    static InMemoryTriageEngine()
    {
        // Seed sample triage assessments for incident INC-1001
        var a1 = new TriageAssessment
        {
            Id = "TRI-9001",
            IncidentId = "INC-1001",
            CasualtyIdentifier = "Patient #1 (Driver, Sedan)",
            AssessedByCallsign = "MEDIC-42",
            Category = TriageCategory.Immediate,
            PrimaryInjury = "Flail chest, severe hypotension, blunt thoracic trauma",
            CanWalk = false,
            HasSpontaneousBreathing = true,
            RadialPulsePresent = false,
            FollowsCommands = false,
            Vitals = new VitalSigns
            {
                HeartRateBpm = 138,
                RespiratoryRateBpm = 34,
                SystolicBp = 78,
                DiastolicBp = 45,
                OxygenSaturationSpO2 = 82,
                GlasgowComaScale = 8,
                BodyTemperatureCelsius = 35.8
            },
            ClinicalNotes = new List<string> { "Needle decompression performed", "Intravenous access established" }
        };

        var a2 = new TriageAssessment
        {
            Id = "TRI-9002",
            IncidentId = "INC-1001",
            CasualtyIdentifier = "Patient #2 (Passenger, Sedan)",
            AssessedByCallsign = "MEDIC-42",
            Category = TriageCategory.Delayed,
            PrimaryInjury = "Compound femur fracture, alert and oriented",
            CanWalk = false,
            HasSpontaneousBreathing = true,
            RadialPulsePresent = true,
            FollowsCommands = true,
            Vitals = new VitalSigns
            {
                HeartRateBpm = 98,
                RespiratoryRateBpm = 18,
                SystolicBp = 122,
                DiastolicBp = 78,
                OxygenSaturationSpO2 = 98,
                GlasgowComaScale = 15,
                BodyTemperatureCelsius = 36.8
            },
            ClinicalNotes = new List<string> { "Traction splint applied", "Morphine 4mg IV administered" }
        };

        var a3 = new TriageAssessment
        {
            Id = "TRI-9003",
            IncidentId = "INC-1001",
            CasualtyIdentifier = "Patient #3 (Driver, Pickup)",
            AssessedByCallsign = "ENGINE-7 EMT",
            Category = TriageCategory.Minor,
            PrimaryInjury = "Superficial facial abrasions from airbag deployment",
            CanWalk = true,
            HasSpontaneousBreathing = true,
            RadialPulsePresent = true,
            FollowsCommands = true,
            Vitals = new VitalSigns
            {
                HeartRateBpm = 82,
                RespiratoryRateBpm = 16,
                SystolicBp = 128,
                DiastolicBp = 82,
                OxygenSaturationSpO2 = 99,
                GlasgowComaScale = 15,
                BodyTemperatureCelsius = 37.0
            },
            ClinicalNotes = new List<string> { "Ambulatory in green triage holding area" }
        };

        Assessments[a1.Id] = a1;
        Assessments[a2.Id] = a2;
        Assessments[a3.Id] = a3;
    }

    public Task<TriageAssessment> AssessCasualtyAsync(StartTriageAssessmentRequest request)
    {
        // Algorithmic START (Simple Triage and Rapid Treatment) Protocol:
        // 1. Can the patient walk? -> Green / Minor
        // 2. Is spontaneous breathing present?
        //    - If No -> Expectant / Black
        //    - If Yes and Rate > 30 or < 10 -> Immediate / Red
        // 3. Radial pulse present or Cap refill <= 2s?
        //    - If No -> Immediate / Red
        // 4. Follows simple commands?
        //    - If No -> Immediate / Red
        //    - If Yes -> Delayed / Yellow

        TriageCategory category;
        if (request.CanWalk)
        {
            category = TriageCategory.Minor;
        }
        else if (!request.HasSpontaneousBreathing)
        {
            category = TriageCategory.Expectant;
        }
        else if (request.RespiratoryRateBpm > 30 || request.RespiratoryRateBpm < 10)
        {
            category = TriageCategory.Immediate;
        }
        else if (!request.RadialPulsePresent || request.CapillaryRefillSeconds > 2.0)
        {
            category = TriageCategory.Immediate;
        }
        else if (!request.FollowsCommands)
        {
            category = TriageCategory.Immediate;
        }
        else
        {
            category = TriageCategory.Delayed;
        }

        var vitals = request.InitialVitals ?? new VitalSigns
        {
            RespiratoryRateBpm = request.RespiratoryRateBpm,
            HeartRateBpm = request.RadialPulsePresent ? 80 : 130,
            OxygenSaturationSpO2 = 95,
            GlasgowComaScale = request.FollowsCommands ? 15 : 8
        };

        var assessment = new TriageAssessment
        {
            Id = $"TRI-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            IncidentId = request.IncidentId,
            CasualtyIdentifier = request.CasualtyIdentifier,
            AssessedByCallsign = request.AssessedByCallsign,
            Category = category,
            PrimaryInjury = request.PrimaryInjury,
            CanWalk = request.CanWalk,
            HasSpontaneousBreathing = request.HasSpontaneousBreathing,
            RadialPulsePresent = request.RadialPulsePresent,
            FollowsCommands = request.FollowsCommands,
            Vitals = vitals,
            ClinicalNotes = string.IsNullOrWhiteSpace(request.Notes) ? new List<string>() : new List<string> { request.Notes },
            AssessedAtUtc = DateTime.UtcNow
        };

        Assessments[assessment.Id] = assessment;
        return Task.FromResult(assessment);
    }

    public Task<TriageAssessment?> GetAssessmentByIdAsync(string id)
    {
        Assessments.TryGetValue(id, out var assessment);
        return Task.FromResult(assessment);
    }

    public Task<IEnumerable<TriageAssessment>> GetAssessmentsByIncidentAsync(string incidentId)
    {
        var list = Assessments.Values.Where(a => a.IncidentId.Equals(incidentId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(list.OrderBy(a => a.Category).AsEnumerable());
    }

    public Task<TriageAssessment?> RecordVitalsAsync(RecordVitalsRequest request)
    {
        if (!Assessments.TryGetValue(request.AssessmentId, out var assessment))
            return Task.FromResult<TriageAssessment?>(null);

        assessment.Vitals = request.Vitals;
        assessment.Vitals.RecordedAtUtc = DateTime.UtcNow;

        // Auto-re-evaluate category if critical vitals drop
        if (request.Vitals.OxygenSaturationSpO2 < 85 || request.Vitals.SystolicBp < 80 || request.Vitals.GlasgowComaScale < 9)
        {
            if (assessment.Category == TriageCategory.Delayed || assessment.Category == TriageCategory.Minor)
            {
                assessment.Category = TriageCategory.Immediate;
                assessment.ClinicalNotes.Add($"Auto-escalated to RED/IMMEDIATE due to critical vitals deterioration at {DateTime.UtcNow:HH:mm:ss} UTC");
            }
        }

        return Task.FromResult<TriageAssessment?>(assessment);
    }

    public Task<IncidentTriageSummary> GetIncidentSummaryAsync(string incidentId)
    {
        var assessments = Assessments.Values
            .Where(a => a.IncidentId.Equals(incidentId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var summary = new IncidentTriageSummary
        {
            IncidentId = incidentId,
            TotalCasualtiesAssessed = assessments.Count,
            ImmediateRedCount = assessments.Count(a => a.Category == TriageCategory.Immediate),
            DelayedYellowCount = assessments.Count(a => a.Category == TriageCategory.Delayed),
            MinorGreenCount = assessments.Count(a => a.Category == TriageCategory.Minor),
            ExpectantBlackCount = assessments.Count(a => a.Category == TriageCategory.Expectant),
            Assessments = assessments,
            LastUpdatedUtc = DateTime.UtcNow
        };

        return Task.FromResult(summary);
    }
}
