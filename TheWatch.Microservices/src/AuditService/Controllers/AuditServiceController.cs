using Microsoft.AspNetCore.Mvc;
using MediatR;
using Dapr;
using TheWatch.Microservices.Evidence.AuditService.Models;
using TheWatch.Microservices.Evidence.AuditService.Services;

namespace TheWatch.Microservices.Evidence.AuditService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuditServiceController : ControllerBase
{
    private readonly ILogger<AuditServiceController> _logger;
    private readonly IAuditLogEngine _engine;

    public AuditServiceController(ILogger<AuditServiceController> logger, IAuditLogEngine engine)
    {
        _logger = logger;
        _engine = engine;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "AuditService", domain = "Evidence", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("records")]
    public async Task<IActionResult> LogRecord([FromBody] LogAuditRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ActorId) || string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest(new { error = "ActorId and Action are required." });
        }

        var record = await _engine.LogEventAsync(request);
        _logger.LogInformation("Logged audit event {Id}: {Actor} -> {Action} on {Target}", record.Id, request.ActorId, request.Action, request.TargetEntityId);
        return Created($"/api/v1/audit/records/{record.Id}", record);
    }

    [HttpGet("records")]
    public async Task<IActionResult> QueryRecords([FromQuery] string? actorId, [FromQuery] string? correlationId, [FromQuery] AuditCategory? category, [FromQuery] string? targetEntityId, [FromQuery] int limit = 50)
    {
        var query = new AuditQueryRequest
        {
            ActorId = actorId,
            CorrelationId = correlationId,
            Category = category,
            TargetEntityId = targetEntityId,
            Limit = limit
        };

        var records = await _engine.QueryRecordsAsync(query);
        return Ok(records);
    }

    [HttpGet("records/{id}")]
    public async Task<IActionResult> GetRecordById(string id)
    {
        var record = await _engine.GetRecordByIdAsync(id);
        if (record == null) return NotFound(new { error = $"Audit record {id} not found." });
        return Ok(record);
    }

    [HttpGet("verify/{id}")]
    public async Task<IActionResult> VerifyIntegrity(string id)
    {
        var result = await _engine.VerifyIntegrityAsync(id);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _engine.GetSummaryAsync();
        return Ok(summary);
    }

    [Topic("thewatch-pubsub", "thewatch.evidence.events")]
    [HttpPost("events")]
    public async Task<IActionResult> HandleDomainEvent([FromBody] object eventPayload)
    {
        _logger.LogInformation("Received domain event in AuditService: {Payload}", eventPayload);
        await _engine.LogEventAsync(new LogAuditRequest
        {
            ActorId = "DaprPubSub",
            ActorRole = "SystemBus",
            Action = "PUBSUB_EVENT_INGESTED",
            Category = AuditCategory.SystemCompliance,
            TargetEntityId = "thewatch-pubsub",
            TargetEntityType = "MessageMesh",
            Details = eventPayload.ToString() ?? "Domain event processed"
        });
        return Ok(new { status = "Audited" });
    }
}