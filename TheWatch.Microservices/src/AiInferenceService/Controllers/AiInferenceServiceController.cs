using Microsoft.AspNetCore.Mvc;
using MediatR;
using Dapr;
using TheWatch.Microservices.AiMl.AiInferenceService.Models;
using TheWatch.Microservices.AiMl.AiInferenceService.Services;

namespace TheWatch.Microservices.AiMl.AiInferenceService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AiInferenceServiceController : ControllerBase
{
    private readonly ILogger<AiInferenceServiceController> _logger;
    private readonly IAiInferenceEngine _engine;

    public AiInferenceServiceController(ILogger<AiInferenceServiceController> logger, IAiInferenceEngine engine)
    {
        _logger = logger;
        _engine = engine;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "AiInferenceService", domain = "AiMl", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("triage-prediction")]
    public async Task<IActionResult> PredictTriage([FromBody] TriagePredictionRequest request)
    {
        var prediction = await _engine.PredictTriageAsync(request);
        return Ok(prediction);
    }

    [HttpPost("incident-classification")]
    public async Task<IActionResult> ClassifyIncident([FromBody] IncidentClassificationRequest request)
    {
        var result = await _engine.ClassifyIncidentAsync(request);
        return Ok(result);
    }

    [HttpPost("anomaly-detection")]
    public async Task<IActionResult> DetectAnomaly([FromBody] AnomalyDetectionRequest request)
    {
        var result = await _engine.DetectAnomalyAsync(request);
        return Ok(result);
    }

    [HttpPost("drone-vision")]
    public async Task<IActionResult> AnalyzeDroneVision([FromBody] DroneVisionAnalysisRequest request)
    {
        var result = await _engine.AnalyzeDroneVisionAsync(request);
        return Ok(result);
    }

    [Topic("thewatch-pubsub", "thewatch.aiml.events")]
    [HttpPost("events")]
    public IActionResult HandleDomainEvent([FromBody] object eventPayload)
    {
        _logger.LogInformation("Received domain event in AiInferenceService: {Payload}", eventPayload);
        return Ok(new { status = "EventConsumed" });
    }
}