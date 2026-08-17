using Microsoft.AspNetCore.Mvc;
using MediatR;
using Dapr;
using TheWatch.Microservices.Notifications.NotificationService.Models;
using TheWatch.Microservices.Notifications.NotificationService.Services;

using TheWatch.Contracts;

namespace TheWatch.Microservices.Notifications.NotificationService.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class NotificationServiceController : ControllerBase
{
    private readonly ILogger<NotificationServiceController> _logger;
    private readonly INotificationService _service;
    private readonly IAlertSyndicatorEngine _syndicator;

    public NotificationServiceController(ILogger<NotificationServiceController> logger, INotificationService service, IAlertSyndicatorEngine syndicator)
    {
        _logger = logger;
        _service = service;
        _syndicator = syndicator;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { service = "NotificationService", domain = "Notifications", status = "Healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("syndicate")]
    public async Task<IActionResult> SyndicateAlert([FromBody] AlertContracts.BroadcastEmergencyAlertRequest request)
    {
        var receipt = await _syndicator.SyndicateBroadcastAsync(request);
        return Ok(receipt);
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientId) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "RecipientId and Body are required." });
        }

        var result = await _service.SendAsync(request);
        _logger.LogInformation("Sent notification {Id} to {Recipient} via {Channel}", result.Id, request.RecipientId, request.Channel);
        return Created($"/api/v1/notifications/{result.Id}", result);
    }

    [HttpPost("broadcast")]
    public async Task<IActionResult> BroadcastAlert([FromBody] BroadcastAlertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Title and Message are required." });
        }

        var result = await _service.BroadcastAsync(request);
        _logger.LogWarning("Dispatched EMERGENCY BROADCAST {Id} to sector {Sector} ({Recipients} units)",
            result.BroadcastId, request.TargetSector, result.RecipientsTargeted);
        return Ok(result);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] string? recipientId, [FromQuery] string? incidentId, [FromQuery] int limit = 50)
    {
        var history = await _service.GetHistoryAsync(recipientId, incidentId, limit);
        return Ok(history);
    }

    [HttpPost("{id}/acknowledge")]
    public async Task<IActionResult> AcknowledgeNotification(string id)
    {
        var ok = await _service.AcknowledgeNotificationAsync(id);
        if (!ok) return NotFound(new { error = $"Notification {id} not found." });
        return Ok(new { status = "Acknowledged", id });
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> RegisterSubscription([FromBody] SubscriptionRequest request)
    {
        await _service.RegisterSubscriptionAsync(request);
        return Ok(new { status = "Subscribed", userId = request.UserId });
    }

    [Topic("thewatch-pubsub", "thewatch.notifications.events")]
    [HttpPost("events")]
    public IActionResult HandleDomainEvent([FromBody] object eventPayload)
    {
        _logger.LogInformation("Received domain event in NotificationService: {Payload}", eventPayload);
        return Ok(new { status = "EventConsumed" });
    }
}