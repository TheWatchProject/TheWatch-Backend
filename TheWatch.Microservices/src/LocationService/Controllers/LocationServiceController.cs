using Microsoft.AspNetCore.Mvc;
using MediatR;
using Dapr;
using TheWatch.Microservices.Location.LocationService.Models;
using TheWatch.Microservices.Location.LocationService.Services;

namespace TheWatch.Microservices.Location.LocationService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class LocationServiceController : ControllerBase
{
    private readonly ILogger<LocationServiceController> _logger;
    private readonly ILocationEngine _engine;

    public LocationServiceController(ILogger<LocationServiceController> logger, ILocationEngine engine)
    {
        _logger = logger;
        _engine = engine;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "LocationService", domain = "Location", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("telemetry")]
    public async Task<IActionResult> RecordTelemetry([FromBody] RecordTelemetryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ResponderId))
        {
            return BadRequest(new { error = "ResponderId is required." });
        }

        var recorded = await _engine.RecordTelemetryAsync(request);
        return Ok(recorded);
    }

    [HttpGet("responders/{id}/current")]
    public async Task<IActionResult> GetCurrentLocation(string id)
    {
        var pos = await _engine.GetCurrentLocationAsync(id);
        if (pos == null) return NotFound(new { error = $"Location not found for responder {id}." });
        return Ok(pos);
    }

    [HttpGet("responders/{id}/history")]
    public async Task<IActionResult> GetLocationHistory(string id, [FromQuery] int limit = 50)
    {
        var history = await _engine.GetLocationHistoryAsync(id, limit);
        return Ok(history);
    }

    [HttpPost("nearby")]
    public async Task<IActionResult> FindNearbyResponders([FromBody] NearbyQueryRequest request)
    {
        var matches = await _engine.FindNearbyRespondersAsync(request);
        return Ok(matches);
    }

    [HttpPost("geofence/check")]
    public async Task<IActionResult> CheckGeofence([FromBody] GeofenceCheckRequest request)
    {
        var result = await _engine.CheckGeofenceAsync(request);
        return Ok(result);
    }

    [HttpGet("geofences")]
    public async Task<IActionResult> GetAllGeofences()
    {
        var zones = await _engine.GetAllGeofencesAsync();
        return Ok(zones);
    }

    [HttpPost("geofences")]
    public async Task<IActionResult> CreateGeofence([FromBody] GeofenceZone zone)
    {
        var created = await _engine.CreateGeofenceAsync(zone);
        return Created($"/api/v1/location/geofences/{created.Id}", created);
    }

    [Topic("thewatch-pubsub", "thewatch.location.events")]
    [HttpPost("events")]
    public IActionResult HandleDomainEvent([FromBody] object eventPayload)
    {
        _logger.LogInformation("Received domain event in LocationService: {Payload}", eventPayload);
        return Ok(new { status = "EventConsumed" });
    }
}