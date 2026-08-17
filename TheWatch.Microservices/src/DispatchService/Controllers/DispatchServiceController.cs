using Microsoft.AspNetCore.Mvc;
using MediatR;
using Dapr;
using TheWatch.Microservices.Dispatch.DispatchService.Models;
using TheWatch.Microservices.Dispatch.DispatchService.Services;

namespace TheWatch.Microservices.Dispatch.DispatchService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class DispatchServiceController : ControllerBase
{
    private readonly ILogger<DispatchServiceController> _logger;
    private readonly IDispatchStore _store;

    public DispatchServiceController(ILogger<DispatchServiceController> logger, IDispatchStore store)
    {
        _logger = logger;
        _store = store;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "DispatchService", domain = "Dispatch", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpGet("units")]
    public async Task<IActionResult> GetUnits([FromQuery] UnitReadiness? status, [FromQuery] UnitType? type)
    {
        var units = await _store.GetAllUnitsAsync(status, type);
        return Ok(units);
    }

    [HttpGet("units/{id}")]
    public async Task<IActionResult> GetUnitById(string id)
    {
        var unit = await _store.GetUnitByIdAsync(id);
        if (unit == null) return NotFound(new { error = $"Unit {id} not found." });
        return Ok(unit);
    }

    [HttpPost("recommend")]
    public async Task<IActionResult> RecommendUnits([FromBody] DispatchRecommendationRequest request)
    {
        var recommendation = await _store.RecommendUnitsAsync(request);
        return Ok(recommendation);
    }

    [HttpPost("assign")]
    public async Task<IActionResult> AssignUnit([FromBody] AssignUnitRequest request)
    {
        try
        {
            var assignment = await _store.AssignUnitAsync(request);
            _logger.LogInformation("Unit {UnitId} dispatched to Incident {IncidentId}", request.UnitId, request.IncidentId);
            return Created($"/api/v1/dispatch/assignments/{assignment.Id}", assignment);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("assignments")]
    public async Task<IActionResult> GetAssignments([FromQuery] string? incidentId, [FromQuery] string? unitId, [FromQuery] bool activeOnly = false)
    {
        var list = await _store.GetAssignmentsAsync(incidentId, unitId, activeOnly);
        return Ok(list);
    }

    [HttpGet("assignments/{id}")]
    public async Task<IActionResult> GetAssignmentById(string id)
    {
        var assignment = await _store.GetAssignmentByIdAsync(id);
        if (assignment == null) return NotFound(new { error = $"Assignment {id} not found." });
        return Ok(assignment);
    }

    [HttpPatch("assignments/{id}/status")]
    public async Task<IActionResult> UpdateAssignmentStatus(string id, [FromBody] UpdateDispatchStatusRequest request)
    {
        var updated = await _store.UpdateAssignmentStatusAsync(id, request);
        if (updated == null) return NotFound(new { error = $"Assignment {id} not found." });
        _logger.LogInformation("Assignment {Id} transitioned to status {Status}", id, request.NewStatus);
        return Ok(updated);
    }

    [HttpPost("assignments/{id}/release")]
    public async Task<IActionResult> ReleaseUnit(string id)
    {
        var success = await _store.ReleaseUnitAsync(id);
        if (!success) return NotFound(new { error = $"Assignment {id} not found." });
        return Ok(new { status = "UnitReleased" });
    }

    [Topic("thewatch-pubsub", "thewatch.dispatch.events")]
    [HttpPost("events")]
    public IActionResult HandleDomainEvent([FromBody] object eventPayload)
    {
        _logger.LogInformation("Received domain event in DispatchService: {Payload}", eventPayload);
        return Ok(new { status = "EventConsumed" });
    }
}