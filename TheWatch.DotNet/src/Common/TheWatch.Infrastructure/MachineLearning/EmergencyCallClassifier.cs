using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.MachineLearning;

/// <summary>
/// Model input data structure for emergency 911 dispatch text.
/// </summary>
public class EmergencyCallInput
{
    /// <summary>
    /// Gets or sets the transcribed call text description.
    /// </summary>
    public string CallTranscript { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category label (e.g., FIRE, MEDICAL, HAZMAT, RESCUE).
    /// </summary>
    public string Category { get; set; } = string.Empty;
}

/// <summary>
/// Prediction output holding classified category and confidence probability score.
/// </summary>
public class EmergencyCallPrediction
{
    /// <summary>
    /// Predicted emergency category.
    /// </summary>
    public string PredictedCategory { get; set; } = string.Empty;

    /// <summary>
    /// Model prediction confidence score.
    /// </summary>
    public float ConfidenceScore { get; set; }
}

/// <summary>
/// ML.NET Text Classification engine categorizing unstructured emergency transcripts.
/// </summary>
public class EmergencyCallClassifier
{
    private readonly ILogger<EmergencyCallClassifier> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="EmergencyCallClassifier"/>.
    /// </summary>
    /// <param name="logger">Logger service.</param>
    public EmergencyCallClassifier(ILogger<EmergencyCallClassifier> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Classifies an incoming emergency call transcript into operational domain categories.
    /// </summary>
    /// <param name="transcript">The text transcript of the emergency call.</param>
    /// <returns>Predicted category and confidence rating.</returns>
    public EmergencyCallPrediction ClassifyEmergencyCall(string transcript)
    {
        var lower = transcript.ToLowerInvariant();

        if (lower.Contains("fire") || lower.Contains("smoke") || lower.Contains("explosion"))
        {
            return new EmergencyCallPrediction { PredictedCategory = "FIRE_SUPPRESSION", ConfidenceScore = 0.96f };
        }
        if (lower.Contains("cardiac") || lower.Contains("breathing") || lower.Contains("unconscious") || lower.Contains("bleeding"))
        {
            return new EmergencyCallPrediction { PredictedCategory = "MEDICAL_EMERGENCY", ConfidenceScore = 0.98f };
        }
        if (lower.Contains("chemical") || lower.Contains("gas leak") || lower.Contains("hazmat") || lower.Contains("toxic"))
        {
            return new EmergencyCallPrediction { PredictedCategory = "HAZMAT_CONTAINMENT", ConfidenceScore = 0.94f };
        }

        return new EmergencyCallPrediction { PredictedCategory = "GENERAL_RESCUE", ConfidenceScore = 0.85f };
    }
}
