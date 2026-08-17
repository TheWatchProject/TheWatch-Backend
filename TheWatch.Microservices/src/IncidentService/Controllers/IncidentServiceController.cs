using Microsoft.AspNetCore.Mvc;
using MediatR;
using Dapr;
using TheWatch.Microservices.Emergency.IncidentService.Models;
using TheWatch.Microservices.Emergency.IncidentService.Services;

namespace TheWatch.Microservices.Emergency.IncidentService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class IncidentServiceController : ControllerBase
{
    private readonly ILogger<IncidentServiceController> _logger;
    private readonly IIncidentStore _store;

    public IncidentServiceController(ILogger<IncidentServiceController> logger, IIncidentStore store)
    {
        _logger = logger;
        _store = store;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "IncidentService", domain = "Emergency", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpGet]
    public async Task<IActionResult> GetIncidents([FromQuery] IncidentStatus? status, [FromQuery] IncidentSeverity? severity, [FromQuery] string? incidentType)
    {
        var incidents = await _store.GetAllAsync(status, severity, incidentType);
        return Ok(incidents);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetIncidentById(string id)
    {
        var incident = await _store.GetByIdAsync(id);
        if (incident == null)
        {
            return NotFound(new { error = $"Incident {id} not found." });
        }
        return Ok(incident);
    }

    [HttpPost]
    public async Task<IActionResult> CreateIncident([FromBody] CreateIncidentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "Title is required for an incident." });
        }

        var created = await _store.CreateAsync(request);
        _logger.LogInformation("Created new emergency incident: {Id} - {Title} ({Severity})", created.Id, created.Title, created.Severity);
        return CreatedAtAction(nameof(GetIncidentById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateIncident(string id, [FromBody] UpdateIncidentRequest request)
    {
        var updated = await _store.UpdateAsync(id, request);
        if (updated == null)
        {
            return NotFound(new { error = $"Incident {id} not found." });
        }
        return Ok(updated);
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateStatusRequest request)
    {
        var updated = await _store.UpdateStatusAsync(id, request);
        if (updated == null)
        {
            return NotFound(new { error = $"Incident {id} not found." });
        }
        _logger.LogInformation("Incident {Id} status transitioned to {Status}", id, request.Status);
        return Ok(updated);
    }

    [HttpPost("{id}/escalate")]
    public async Task<IActionResult> EscalateIncident(string id, [FromBody] EscalateIncidentRequest request)
    {
        var escalated = await _store.EscalateAsync(id, request);
        if (escalated == null)
        {
            return NotFound(new { error = $"Incident {id} not found." });
        }
        _logger.LogWarning("Incident {Id} escalated to {Severity} by {Actor}", id, request.TargetSeverity, request.EscalatedBy);
        return Ok(escalated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteIncident(string id)
    {
        var deleted = await _store.DeleteAsync(id);
        if (!deleted)
        {
            return NotFound(new { error = $"Incident {id} not found." });
        }
        return NoContent();
    }

    [Topic("thewatch-pubsub", "thewatch.emergency.events")]
    [HttpPost("events")]
    public IActionResult HandleDomainEvent([FromBody] object eventPayload)
    {
        _logger.LogInformation("Received domain event in IncidentService: {Payload}", eventPayload);
        return Ok(new { status = "EventConsumed" });
    }
}