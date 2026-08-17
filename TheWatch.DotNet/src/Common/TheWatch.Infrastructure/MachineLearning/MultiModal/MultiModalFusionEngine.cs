using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.MachineLearning.MultiModal;

/// <summary>
/// Comprehensive fused situational awareness vector combining Audio, Video, Image, Text, and GPS Telemetry.
/// </summary>
/// <param name="IncidentId">Associated incident identifier.</param>
/// <param name="ThreatLevelScore">Overall fused threat score (0.0 to 1.0).</param>
/// <param name="AudioSummary">Summary of acoustic findings.</param>
/// <param name="VisionSummary">Summary of computer vision findings.</param>
/// <param name="NlpSummary">Summary of text and entity findings.</param>
/// <param name="FusedRecommendation">Recommended tactical action.</param>
/// <param name="GeneratedAt">UTC timestamp of fusion.</param>
public record FusedSituationalAwareness(
    string IncidentId,
    float ThreatLevelScore,
    string AudioSummary,
    string VisionSummary,
    string NlpSummary,
    string FusedRecommendation,
    DateTime GeneratedAt
);

/// <summary>
/// Central Multi-Modal AI Fusion Engine synthesizing multi-sensory data streams into unified intelligence.
/// </summary>
public class MultiModalFusionEngine
{
    private readonly AudioAnalysisPipeline _audioPipeline;
    private readonly VisionAnalysisPipeline _visionPipeline;
    private readonly TextNlpPipeline _nlpPipeline;
    private readonly ILogger<MultiModalFusionEngine> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="MultiModalFusionEngine"/>.
    /// </summary>
    public MultiModalFusionEngine(
        AudioAnalysisPipeline audioPipeline,
        VisionAnalysisPipeline visionPipeline,
        TextNlpPipeline nlpPipeline,
        ILogger<MultiModalFusionEngine> logger)
    {
        _audioPipeline = audioPipeline;
        _visionPipeline = visionPipeline;
        _nlpPipeline = nlpPipeline;
        _logger = logger;
    }

    /// <summary>
    /// Fuses sensory data streams across audio, video frames, and textual reports into actionable dispatch intelligence.
    /// </summary>
    /// <param name="incidentId">Target incident identifier.</param>
    /// <param name="audioBytes">Audio stream segment.</param>
    /// <param name="videoFrameBytes">Camera frame bytes.</param>
    /// <param name="reportText">Text narrative or 911 transcript.</param>
    /// <returns>A unified <see cref="FusedSituationalAwareness"/> assessment.</returns>
    public FusedSituationalAwareness FuseIncidentStreams(
        string incidentId,
        byte[] audioBytes,
        byte[] videoFrameBytes,
        string reportText)
    {
        var audioResult = _audioPipeline.AnalyzeAudioStream(audioBytes);
        var visionResult = _visionPipeline.ProcessVideoFrame(videoFrameBytes, isThermalFrame: true);
        var (entities, nlpSummary) = _nlpPipeline.ExtractEntitiesAndSummary(reportText);

        // Fused Threat Calculation
        float threatScore = 0.5f;
        if (visionResult.ContainsActiveFire) threatScore += 0.3f;
        if (audioResult.DetectedEventType.Contains("EXPLOSION")) threatScore += 0.2f;
        threatScore = Math.Min(1.0f, threatScore);

        var recommendation = threatScore > 0.8f
            ? "IMMEDIATE 3-ALARM DISPATCH: Deploy Hazmat Containment, 2 Heavy Ambulances, and Autonomous Drone Recon."
            : "STANDARD DISPATCH: Single Paramedic Unit en route.";

        _logger.LogWarning("MULTI-MODAL FUSION COMPLETE for Incident {IncidentId}: Threat Level={Threat:F2}", incidentId, threatScore);

        return new FusedSituationalAwareness(
            incidentId,
            threatScore,
            $"Acoustic: {audioResult.DetectedEventType} ({audioResult.DecibelLevel:F0}dB)",
            $"Vision: {visionResult.DetectedObjects.Count} objects detected (Hotspot: {visionResult.MaxThermalTempCelsius}°C)",
            nlpSummary,
            recommendation,
            DateTime.UtcNow
        );
    }
}
