using TheWatch.Microservices.AiMl.AiInferenceService.Models;

namespace TheWatch.Microservices.AiMl.AiInferenceService.Services;

public interface IAiInferenceEngine
{
    Task<TriagePredictionResponse> PredictTriageAsync(TriagePredictionRequest request);
    Task<IncidentClassificationResponse> ClassifyIncidentAsync(IncidentClassificationRequest request);
    Task<AnomalyDetectionResponse> DetectAnomalyAsync(AnomalyDetectionRequest request);
    Task<DroneVisionAnalysisResponse> AnalyzeDroneVisionAsync(DroneVisionAnalysisRequest request);
}

public class AiInferenceEngine : IAiInferenceEngine
{
    public Task<TriagePredictionResponse> PredictTriageAsync(TriagePredictionRequest request)
    {
        var text = $"{request.IncidentDescription} {request.CallerTranscript}".ToLowerInvariant();

        string predictedSeverity = "Moderate";
        double confidence = 0.88;
        string primaryRisk = "GeneralMedical";
        var equipment = new List<string> { "Standard First Aid Kit", "Oxygen Tank" };
        var escalationFactors = new List<string>();
        double traumaProb = 0.25;

        if (text.Contains("cardiac") || text.Contains("heart attack") || text.Contains("unresponsive") || text.Contains("cpr") || text.Contains("not breathing"))
        {
            predictedSeverity = "Critical";
            confidence = 0.96;
            primaryRisk = "Sudden Cardiac Arrest";
            equipment.AddRange(new[] { "Automated External Defibrillator (AED)", "Mechanical CPR (Lucas)", "Advanced Cardiac Life Support Kit" });
            escalationFactors.Add("Immediate life threat: Time to defibrillation directly impacts survival");
            traumaProb = 0.85;
        }
        else if (text.Contains("fire") || text.Contains("smoke") || text.Contains("trapped") || text.Contains("explosion") || text.Contains("flames"))
        {
            predictedSeverity = "Critical";
            confidence = 0.94;
            primaryRisk = "Thermal / Respiratory Inhalation Hazard";
            equipment.AddRange(new[] { "SCBA Respirators", "Thermal Imager", "Heavy Extrication Jaws", "Burn Trauma Kits" });
            escalationFactors.Add("Active structure fire with potential occupant entrapment");
            traumaProb = 0.70;
        }
        else if (text.Contains("crash") || text.Contains("collision") || text.Contains("rollover") || text.Contains("pileup") || request.ReportedInjuredCount >= 3)
        {
            predictedSeverity = request.ReportedInjuredCount >= 4 ? "Disaster" : "Critical";
            confidence = 0.92;
            primaryRisk = "Multi-Casualty Blunt Force Trauma";
            equipment.AddRange(new[] { "Triage Tarps (START Kit)", "Spinal Immobilization Boards", "Tourniquets & Hemostatic Gauze" });
            escalationFactors.Add($"Multiple casualties ({request.ReportedInjuredCount}) requiring mass casualty incident (MCI) protocols");
            traumaProb = 0.90;
        }
        else if (text.Contains("bleeding") || text.Contains("fracture") || text.Contains("fall"))
        {
            predictedSeverity = "High";
            confidence = 0.89;
            primaryRisk = "Orthopedic / Hemorrhage";
            equipment.AddRange(new[] { "Traction Splints", "Pressure Bandages", "IV Fluid Resuscitation" });
            traumaProb = 0.55;
        }

        var response = new TriagePredictionResponse
        {
            PredictedSeverity = predictedSeverity,
            SeverityConfidence = confidence,
            PrimaryRiskCategory = primaryRisk,
            RecommendedEquipment = equipment.Distinct().ToList(),
            EscalationFactors = escalationFactors,
            EstimatedTraumaProbability = traumaProb,
            SummaryAnalysis = $"AI inference model predicts {predictedSeverity} severity with {confidence * 100:F0}% confidence. Primary diagnosis: {primaryRisk}."
        };

        return Task.FromResult(response);
    }

    public Task<IncidentClassificationResponse> ClassifyIncidentAsync(IncidentClassificationRequest request)
    {
        var text = request.RawTextOrTranscript.ToLowerInvariant();
        string incidentType = "Medical";
        double confidence = 0.85;
        string urgency = "ROUTINE";
        bool weapon = false;
        bool hazmat = false;
        var entities = new List<string>();

        if (text.Contains("gun") || text.Contains("shot") || text.Contains("knife") || text.Contains("stab") || text.Contains("assault"))
        {
            weapon = true;
            urgency = "FLASH";
            entities.Add("Potential Weapon Involved");
        }

        if (text.Contains("chemical") || text.Contains("gas leak") || text.Contains("fumes") || text.Contains("chlorine") || text.Contains("acid"))
        {
            hazmat = true;
            incidentType = "HazMat";
            urgency = "FLASH";
            confidence = 0.95;
            entities.Add("Hazardous Material / Airborne Toxin");
        }
        else if (text.Contains("fire") || text.Contains("smoke") || text.Contains("flames"))
        {
            incidentType = "StructureFire";
            urgency = "URGENT";
            confidence = 0.93;
            entities.Add("Active Combustion");
        }
        else if (text.Contains("car") || text.Contains("vehicle") || text.Contains("collision") || text.Contains("highway"))
        {
            incidentType = "Collision";
            urgency = "URGENT";
            confidence = 0.91;
            entities.Add("Motor Vehicle Collision");
        }
        else if (text.Contains("lost") || text.Contains("trail") || text.Contains("hiking") || text.Contains("mountain"))
        {
            incidentType = "Rescue";
            urgency = "PRIORITY";
            confidence = 0.88;
            entities.Add("Search and Rescue Domain");
        }

        return Task.FromResult(new IncidentClassificationResponse
        {
            IncidentType = incidentType,
            Confidence = confidence,
            ExtractedEntities = entities,
            UrgencyScore = urgency,
            WeaponOrViolenceDetected = weapon,
            HazmatHazardDetected = hazmat
        });
    }

    public Task<AnomalyDetectionResponse> DetectAnomalyAsync(AnomalyDetectionRequest request)
    {
        double stdDev = request.StandardDeviation <= 0 ? 1.0 : request.StandardDeviation;
        double zScore = (request.CurrentValue - request.BaselineAverage) / stdDev;
        double absZ = Math.Abs(zScore);

        bool isAnomaly = absZ >= 2.5;
        double scorePercent = Math.Min(100.0, Math.Round((absZ / 4.0) * 100.0, 1));
        string classification = "Normal";
        string intervention = "No action required. Telemetry within nominal bounds.";

        if (absZ >= 4.0)
        {
            classification = "CriticalFailure";
            intervention = "IMMEDIATE EVACUATION / ALS INTERVENTION: Telemetry is 4+ standard deviations outside normal envelope.";
        }
        else if (absZ >= 3.0)
        {
            classification = "SevereAnomaly";
            intervention = "High Priority Alert: Dispatch secondary backup and initiate sensor cross-verification.";
        }
        else if (absZ >= 2.0)
        {
            classification = "Warning";
            intervention = "Monitor closely: Minor telemetry variance detected.";
        }

        return Task.FromResult(new AnomalyDetectionResponse
        {
            IsAnomaly = isAnomaly,
            ZScore = Math.Round(zScore, 2),
            AnomalyScorePercentage = scorePercent,
            AnomalyClassification = classification,
            RecommendedIntervention = intervention
        });
    }

    public Task<DroneVisionAnalysisResponse> AnalyzeDroneVisionAsync(DroneVisionAnalysisRequest request)
    {
        bool thermalHotspot = request.ThermalMaxTempCelsius > 60.0;
        int humanCount = request.ThermalMaxTempCelsius > 40.0 ? 3 : 1;
        var objects = new List<DetectedObject>();

        if (thermalHotspot)
        {
            objects.Add(new DetectedObject
            {
                Label = "FireHotspot",
                Confidence = 0.97,
                BoundingBoxX = 0.42,
                BoundingBoxY = 0.35,
                BoundingBoxWidth = 0.25,
                BoundingBoxHeight = 0.30
            });
            objects.Add(new DetectedObject
            {
                Label = "SmokePlume",
                Confidence = 0.94,
                BoundingBoxX = 0.35,
                BoundingBoxY = 0.10,
                BoundingBoxWidth = 0.40,
                BoundingBoxHeight = 0.30
            });
        }

        for (int i = 0; i < humanCount; i++)
        {
            objects.Add(new DetectedObject
            {
                Label = "HumanVictim",
                Confidence = 0.91 + (i * 0.02),
                BoundingBoxX = 0.20 + (i * 0.25),
                BoundingBoxY = 0.65,
                BoundingBoxWidth = 0.08,
                BoundingBoxHeight = 0.15
            });
        }

        string hazard = thermalHotspot ? "Extreme Thermal & Structural Hazard" : "Moderate Operational Hazard";

        return Task.FromResult(new DroneVisionAnalysisResponse
        {
            DroneUnitId = request.DroneUnitId,
            DetectedObjects = objects,
            ThermalHotspotDetected = thermalHotspot,
            MaxTemperatureCelsius = request.ThermalMaxTempCelsius,
            HumanCountEstimate = humanCount,
            SceneHazardAssessment = hazard,
            ProcessedAtUtc = DateTime.UtcNow
        });
    }
}
