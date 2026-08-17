namespace TheWatch.Microservices.AiMl.AiInferenceService.Models;

public class TriagePredictionRequest
{
    public string IncidentDescription { get; set; } = string.Empty;
    public string CallerTranscript { get; set; } = string.Empty;
    public int ReportedInjuredCount { get; set; }
    public string LocationContext { get; set; } = string.Empty;
    public Dictionary<string, string>? SensorData { get; set; }
}

public class TriagePredictionResponse
{
    public string PredictedSeverity { get; set; } = "High"; // Low, Moderate, High, Critical, Disaster
    public double SeverityConfidence { get; set; }
    public string PrimaryRiskCategory { get; set; } = string.Empty;
    public List<string> RecommendedEquipment { get; set; } = new();
    public List<string> EscalationFactors { get; set; } = new();
    public double EstimatedTraumaProbability { get; set; }
    public string SummaryAnalysis { get; set; } = string.Empty;
}

public class IncidentClassificationRequest
{
    public string RawTextOrTranscript { get; set; } = string.Empty;
    public string AudioSourceId { get; set; } = string.Empty;
}

public class IncidentClassificationResponse
{
    public string IncidentType { get; set; } = "Medical"; // Medical, StructureFire, Wildfire, HazMat, Collision, Rescue
    public double Confidence { get; set; }
    public List<string> ExtractedEntities { get; set; } = new();
    public string UrgencyScore { get; set; } = "URGENT"; // ROUTINE, PRIORITY, URGENT, FLASH
    public bool WeaponOrViolenceDetected { get; set; }
    public bool HazmatHazardDetected { get; set; }
}

public class AnomalyDetectionRequest
{
    public string SensorId { get; set; } = string.Empty;
    public string TelemetryType { get; set; } = "Vitals"; // Vitals, AirQuality, DroneThermal, Structural
    public double CurrentValue { get; set; }
    public double BaselineAverage { get; set; }
    public double StandardDeviation { get; set; }
    public Dictionary<string, double>? MultiVariateReadings { get; set; }
}

public class AnomalyDetectionResponse
{
    public bool IsAnomaly { get; set; }
    public double ZScore { get; set; }
    public double AnomalyScorePercentage { get; set; }
    public string AnomalyClassification { get; set; } = "Normal"; // Normal, Warning, SevereAnomaly, CriticalFailure
    public string RecommendedIntervention { get; set; } = string.Empty;
}

public class DroneVisionAnalysisRequest
{
    public string DroneUnitId { get; set; } = string.Empty;
    public string ImageBase64OrUri { get; set; } = string.Empty;
    public double FlightAltitudeMeters { get; set; }
    public double ThermalMaxTempCelsius { get; set; }
}

public class DetectedObject
{
    public string Label { get; set; } = string.Empty; // Human, Vehicle, FireHotspot, SmokePlume, FloodWater
    public double Confidence { get; set; }
    public double BoundingBoxX { get; set; }
    public double BoundingBoxY { get; set; }
    public double BoundingBoxWidth { get; set; }
    public double BoundingBoxHeight { get; set; }
}

public class DroneVisionAnalysisResponse
{
    public string DroneUnitId { get; set; } = string.Empty;
    public List<DetectedObject> DetectedObjects { get; set; } = new();
    public bool ThermalHotspotDetected { get; set; }
    public double MaxTemperatureCelsius { get; set; }
    public int HumanCountEstimate { get; set; }
    public string SceneHazardAssessment { get; set; } = "Low Hazard";
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}
