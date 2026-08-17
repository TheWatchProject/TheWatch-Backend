using Microsoft.AspNetCore.Mvc;
using MediatR;
using Dapr;
using TheWatch.Microservices.Medical.TriageService.Models;
using TheWatch.Microservices.Medical.TriageService.Services;

using TheWatch.Contracts;

namespace TheWatch.Microservices.Medical.TriageService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class TriageServiceController : ControllerBase
{
    private readonly ILogger<TriageServiceController> _logger;
    private readonly ITriageEngine _engine;
    private readonly IBiometricTriageEvaluator _biometrics;

    public TriageServiceController(ILogger<TriageServiceController> logger, ITriageEngine engine, IBiometricTriageEvaluator biometrics)
    {
        _logger = logger;
        _engine = engine;
        _biometrics = biometrics;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "TriageService", domain = "Medical", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("biometrics/vitals")]
    public async Task<IActionResult> IngestVitalSigns([FromBody] BiometricContracts.VitalSignsSample sample)
    {
        var status = await _biometrics.IngestVitalSignsAsync(sample);
        return Ok(status);
    }

    [HttpPost("biometrics/fall-alert")]
    public async Task<IActionResult> ProcessFallAlert([FromBody] BiometricContracts.FallDetectionAlert alert)
    {
        var status = await _biometrics.ProcessFallAlertAsync(alert);
        return Ok(status);
    }

    [HttpGet("biometrics/man-down")]
    public async Task<IActionResult> GetManDownStatuses()
    {
        var list = await _biometrics.GetActiveManDownStatusesAsync();
        return Ok(list);
    }

    [HttpPost("assess")]
    public async Task<IActionResult> AssessCasualty([FromBody] StartTriageAssessmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IncidentId))
        {
            return BadRequest(new { error = "IncidentId is required." });
        }

        var assessment = await _engine.AssessCasualtyAsync(request);
        _logger.LogInformation("Assessed casualty {Casualty} for incident {Incident}: Category {Category}",
            request.CasualtyIdentifier, request.IncidentId, assessment.Category);
        return Created($"/api/v1/triage/assessments/{assessment.Id}", assessment);
    }

    [HttpGet("assessments")]
    public async Task<IActionResult> GetAssessments([FromQuery] string? incidentId)
    {
        if (string.IsNullOrWhiteSpace(incidentId))
        {
            return BadRequest(new { error = "incidentId query parameter is required." });
        }

        var list = await _engine.GetAssessmentsByIncidentAsync(incidentId);
        return Ok(list);
    }

    [HttpGet("assessments/{id}")]
    public async Task<IActionResult> GetAssessmentById(string id)
    {
        var assessment = await _engine.GetAssessmentByIdAsync(id);
        if (assessment == null) return NotFound(new { error = $"Assessment {id} not found." });
        return Ok(assessment);
    }

    [HttpPost("vitals")]
    public async Task<IActionResult> RecordVitals([FromBody] RecordVitalsRequest request)
    {
        var updated = await _engine.RecordVitalsAsync(request);
        if (updated == null) return NotFound(new { error = $"Assessment {request.AssessmentId} not found." });
        return Ok(updated);
    }

    [HttpGet("summary/{incidentId}")]
    public async Task<IActionResult> GetSummary(string incidentId)
    {
        var summary = await _engine.GetIncidentSummaryAsync(incidentId);
        return Ok(summary);
    }

    [Topic("thewatch-pubsub", "thewatch.medical.events")]
    [HttpPost("events")]
    public IActionResult HandleDomainEvent([FromBody] object eventPayload)
    {
        _logger.LogInformation("Received domain event in TriageService: {Payload}", eventPayload);
        return Ok(new { status = "EventConsumed" });
    }
}