using System.Collections.Concurrent;
using TheWatch.Contracts;
using static TheWatch.Contracts.ClinicalDiagnosticContracts;

namespace TheWatch.Geospatial.Db;

public interface IClinicalDiagnosticAndTriageEngine
{
    void RegisterIcd10Code(Icd10DiagnosticEntry entry);
    RevisedTraumaScore CalculateRevisedTraumaScore(int gcsScore, int systolicBloodPressure, int respiratoryRate);
    JumpStartPediatricTriage EvaluateJumpStart(string casualtyId, bool canWalk, bool breathing, bool breathingAfterPosition, int respRate, bool pulse, string avpu);
    ClinicalTriageEvaluation EvaluateClinicalPresentation(string casualtyId, string presentingSymptom, GlasgowComaScaleAssessment gcs, int sbp, int rr);
    IReadOnlyList<Icd10DiagnosticEntry> SearchIcd10BySymptom(string query);
}

public sealed class ClinicalDiagnosticAndTriageEngine : IClinicalDiagnosticAndTriageEngine
{
    private readonly ConcurrentDictionary<string, Icd10DiagnosticEntry> _icd10Entries = new();

    public ClinicalDiagnosticAndTriageEngine()
    {
        SeedStandardIcd10Entries();
    }

    private void SeedStandardIcd10Entries()
    {
        var entries = new List<Icd10DiagnosticEntry>
        {
            new("I21.9", "Acute myocardial infarction, unspecified", "Cardiovascular", 5, 4.5,
                new List<string> { "Aspirin 324mg", "Nitroglycerin 0.4mg SL", "12-Lead ECG", "Automated External Defibrillator (AED)" }),

            new("S06.9X0A", "Unspecified intracranial injury without loss of consciousness, initial encounter", "Trauma", 4, 3.2,
                new List<string> { "Cervical Collar", "Cranial CT Scanner", "Intracranial Pressure Monitor" }),

            new("S06.9X9A", "Unspecified intracranial injury with loss of consciousness of unspecified duration, initial encounter", "Trauma", 5, 8.5,
                new List<string> { "Endotracheal Tube", "Mechanical Ventilator", "Emergency Neurosurgical Suite" }),

            new("T20.0", "Burn of unspecified degree of head, face, and neck", "Trauma", 4, 6.0,
                new List<string> { "Sterile Burn Sheet", "Lactated Ringer's IV Infusion", "Burn Intensive Care Unit" }),

            new("T71.9", "Asphyxiation, unspecified, accidental", "Respiratory", 5, 5.0,
                new List<string> { "High-Flow Oxygen Mask", "Bag-Valve-Mask Resuscitator", "Capnography" }),

            new("R57.9", "Shock, unspecified", "Cardiovascular", 5, 7.0,
                new List<string> { "0.9% Normal Saline Bolus", "Tranexamic Acid (TXA)", "Whole Blood Type O-Neg", "Rapid Infuser" }),

            new("R07.9", "Chest pain, unspecified", "Cardiovascular", 3, 1.5,
                new List<string> { "12-Lead ECG", "Continuous Cardiac Monitor", "Pulse Oximeter" })
        };

        foreach (var e in entries)
        {
            _icd10Entries.TryAdd(e.Code, e);
        }
    }

    public void RegisterIcd10Code(Icd10DiagnosticEntry entry)
    {
        _icd10Entries[entry.Code] = entry;
    }

    public RevisedTraumaScore CalculateRevisedTraumaScore(int gcsScore, int systolicBloodPressure, int respiratoryRate)
    {
        // RTS Coded values (0 to 4)
        int gcsCode = gcsScore switch { >= 13 => 4, >= 9 => 3, >= 6 => 2, >= 4 => 1, _ => 0 };
        int sbpCode = systolicBloodPressure switch { > 89 => 4, >= 76 => 3, >= 50 => 2, >= 1 => 1, _ => 0 };
        int rrCode = respiratoryRate switch { >= 10 and <= 29 => 4, > 29 => 3, >= 6 => 2, >= 1 => 1, _ => 0 };

        // RTS Formula: RTS = 0.9368 * GCS + 0.7326 * SBP + 0.2908 * RR
        double score = (0.9368 * gcsCode) + (0.7326 * sbpCode) + (0.2908 * rrCode);
        score = Math.Round(score, 4);

        // Survival probability approximation based on RTS score
        double survivalProb = score switch
        {
            >= 7.0 => 98.0,
            >= 6.0 => 91.0,
            >= 5.0 => 80.0,
            >= 4.0 => 60.0,
            >= 3.0 => 36.0,
            _ => 10.0
        };

        bool requiresTrauma1 = score < 6.8 || gcsScore <= 8 || systolicBloodPressure < 90;

        return new RevisedTraumaScore(score, survivalProb, requiresTrauma1);
    }

    public JumpStartPediatricTriage EvaluateJumpStart(
        string casualtyId,
        bool canWalk,
        bool breathing,
        bool breathingAfterPosition,
        int respRate,
        bool pulse,
        string avpu)
    {
        if (canWalk)
        {
            return new JumpStartPediatricTriage(casualtyId, true, true, false, respRate, true, "Alert", 3, "Green (Minor)");
        }

        if (!breathing && !breathingAfterPosition)
        {
            if (!pulse)
            {
                return new JumpStartPediatricTriage(casualtyId, false, false, false, 0, false, avpu, 4, "Black (Deceased / Expectant)");
            }
        }

        if (respRate is < 15 or > 45 || !pulse || avpu is "Pain" or "Unresponsive")
        {
            return new JumpStartPediatricTriage(casualtyId, false, breathing, breathingAfterPosition, respRate, pulse, avpu, 1, "Red (Immediate)");
        }

        return new JumpStartPediatricTriage(casualtyId, false, true, false, respRate, pulse, avpu, 2, "Yellow (Delayed)");
    }

    public ClinicalTriageEvaluation EvaluateClinicalPresentation(
        string casualtyId,
        string presentingSymptom,
        GlasgowComaScaleAssessment gcs,
        int sbp,
        int rr)
    {
        var matched = SearchIcd10BySymptom(presentingSymptom).FirstOrDefault()
            ?? _icd10Entries.Values.First();

        var rts = CalculateRevisedTraumaScore(gcs.TotalScore, sbp, rr);

        // Linear regression / regression model for patient bed days:
        // Base days + GCS deficit + severity factor
        double gcsDeficit = Math.Max(0, (15 - gcs.TotalScore) * 0.4);
        double sbpDeficit = sbp < 90 ? 2.5 : 0.0;
        double predictedBedDays = Math.Round(matched.EstimatedLengthOfStayDays + gcsDeficit + sbpDeficit, 1);

        int recommendedTraumaLevel = rts.RequiresTraumaCenterLevel1 ? 1 : 3;

        return new ClinicalTriageEvaluation(
            casualtyId,
            matched.Code,
            matched.Description,
            gcs,
            rts,
            predictedBedDays,
            recommendedTraumaLevel,
            matched.RecommendedMedicationsOrEquipment,
            DateTime.UtcNow
        );
    }

    public IReadOnlyList<Icd10DiagnosticEntry> SearchIcd10BySymptom(string query)
    {
        var q = query.ToLowerInvariant();
        return _icd10Entries.Values
            .Where(e => e.Description.ToLowerInvariant().Contains(q) || e.ClinicalCategory.ToLowerInvariant().Contains(q))
            .ToList();
    }
}
