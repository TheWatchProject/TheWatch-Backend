using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for HQ broadcast operations ("Voice of God" alerts).
/// Critical safety commands sent to all responders in an incident.
/// </summary>
/// <remarks>
/// Key Features:
/// - Critical alerts to all assigned responders
/// - Delivery confirmation tracking
/// - Automatic retry for failed deliveries
/// - High-priority notification channel
/// - SMS fallback when push unavailable
/// - Text-to-Speech (TTS) support for audio alerts
///
/// Use Cases:
/// - "WEAPON SPOTTED - RETREAT"
/// - "MEDICAL EMERGENCY - PARAMEDICS DISPATCHED"
/// - "POLICE ARRIVING IN 2 MINUTES"
/// - "STAND DOWN - SITUATION RESOLVED"
/// </remarks>
public class HqBroadcastFunctions
{
    private readonly ILogger<HqBroadcastFunctions> _logger;

    public HqBroadcastFunctions(ILogger<HqBroadcastFunctions> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// POST /hq/broadcast - Send "Voice of God" broadcast to responders
    /// </summary>
    /// <remarks>
    /// HQ/Admin only endpoint.
    /// Sends critical alert to all responders assigned to an incident.
    /// Supports:
    /// - Text message display
    /// - Audio TTS playback
    /// - Audio clip playback (pre-recorded)
    /// - SMS fallback if push fails
    /// - Delivery confirmation tracking
    /// </remarks>
    [Function("SendHqBroadcast")]
    public async Task<SendHqBroadcastResult> SendHqBroadcast(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/broadcast")] HttpRequestData req)
    {
        _logger.LogInformation("Sending HQ broadcast");

        try
        {
            // TODO: Validate HQ/Admin role from JWT claims
            // Extract user ID and role for audit logging

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var broadcastRequest = JsonSerializer.Deserialize<HqBroadcastRequest>(requestBody);

            if (broadcastRequest == null || string.IsNullOrEmpty(broadcastRequest.IncidentId) ||
                string.IsNullOrEmpty(broadcastRequest.Message))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid broadcast request" });
                return new SendHqBroadcastResult { HttpResponse = badRequest };
            }

            // TODO: Implement HQ broadcast logic
            // 1. Validate authentication (HQ/Admin only)
            // 2. Verify incident exists and is active
            // 3. Query all responders assigned to incident
            // 4. Generate broadcast ID for tracking
            // 5. Queue critical priority notification for each responder
            // 6. Broadcast via SignalR to 'hq-broadcasts' channel
            // 7. Track delivery confirmations in database
            // 8. If SMS fallback enabled, send SMS to responders without active push connection
            // 9. Log broadcast to incident timeline and admin audit log
            // 10. Return broadcast ID and recipient count

            var broadcastId = Guid.NewGuid().ToString();
            var timestamp = DateTime.UtcNow;

            // Create SignalR message to broadcast to incident group
            var signalRMessages = new List<SignalRMessageAction>
            {
                new SignalRMessageAction("hqBroadcast")
                {
                    GroupName = $"incident-{broadcastRequest.IncidentId}",
                    Arguments = new object[]
                    {
                        new
                        {
                            broadcastId = broadcastId,
                            incidentId = broadcastRequest.IncidentId,
                            message = broadcastRequest.Message,
                            command = broadcastRequest.Command,
                            severity = broadcastRequest.Severity,
                            includeAudioTts = broadcastRequest.IncludeAudioTts,
                            audioClipUrl = broadcastRequest.AudioClipUrl,
                            timestamp = timestamp
                        }
                    }
                },
                new SignalRMessageAction("hqBroadcast")
                {
                    GroupName = "hq-broadcasts",
                    Arguments = new object[]
                    {
                        new
                        {
                            broadcastId = broadcastId,
                            incidentId = broadcastRequest.IncidentId,
                            message = broadcastRequest.Message,
                            command = broadcastRequest.Command,
                            severity = broadcastRequest.Severity,
                            timestamp = timestamp
                        }
                    }
                }
            };

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                broadcastId = broadcastId,
                incidentId = broadcastRequest.IncidentId,
                status = "queued",
                recipientCount = 2, // Placeholder - should query actual responder count
                timestamp = timestamp,
                deliveryConfirmations = new
                {
                    expected = 2,
                    confirmed = 0,
                    failed = 0
                }
            });

            return new SendHqBroadcastResult
            {
                HttpResponse = response,
                SignalRMessages = signalRMessages.ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending HQ broadcast");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return new SendHqBroadcastResult { HttpResponse = errorResponse };
        }
    }

    /// <summary>
    /// GET /hq/broadcast/{broadcastId}/status - Get broadcast delivery status
    /// </summary>
    /// <remarks>
    /// Returns delivery confirmation status for a broadcast.
    /// Shows which responders received/acknowledged the broadcast.
    /// Used by HQ to verify critical messages were delivered.
    /// </remarks>
    [Function("GetBroadcastStatus")]
    public async Task<HttpResponseData> GetBroadcastStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hq/broadcast/{broadcastId}/status")] HttpRequestData req,
        string broadcastId)
    {
        _logger.LogInformation("Getting broadcast status: {BroadcastId}", broadcastId);

        try
        {
            // TODO: Implement broadcast status retrieval
            // 1. Validate HQ/Admin authentication
            // 2. Query broadcast record and delivery confirmations
            // 3. Return delivery status for each recipient

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                broadcastId = broadcastId,
                status = "delivered",
                sentAt = DateTime.UtcNow.AddMinutes(-5),
                recipients = new object[]
                {
                    new
                    {
                        responderId = Guid.NewGuid().ToString(),
                        deliveryStatus = "delivered",
                        deliveredAt = DateTime.UtcNow.AddMinutes(-4),
                        acknowledgedAt = (DateTime?)DateTime.UtcNow.AddMinutes(-4),
                        channel = "push"
                    },
                    new
                    {
                        responderId = Guid.NewGuid().ToString(),
                        deliveryStatus = "delivered",
                        deliveredAt = DateTime.UtcNow.AddMinutes(-3),
                        acknowledgedAt = (DateTime?)null,
                        channel = "sms"
                    }
                },
                deliveryConfirmations = new
                {
                    expected = 2,
                    confirmed = 2,
                    failed = 0,
                    pending = 0
                }
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting broadcast status for {BroadcastId}", broadcastId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /hq/broadcast/{broadcastId}/confirm - Confirm broadcast delivery
    /// </summary>
    /// <remarks>
    /// Called by responder clients when they receive and acknowledge a broadcast.
    /// Tracks delivery confirmations for audit and retry logic.
    /// </remarks>
    [Function("ConfirmBroadcastDelivery")]
    public async Task<HttpResponseData> ConfirmBroadcastDelivery(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/broadcast/{broadcastId}/confirm")] HttpRequestData req,
        string broadcastId)
    {
        _logger.LogInformation("Confirming broadcast delivery: {BroadcastId}", broadcastId);

        try
        {
            // TODO: Implement delivery confirmation
            // 1. Validate authentication (responder)
            // 2. Extract responder ID from JWT
            // 3. Record delivery confirmation timestamp
            // 4. Update delivery status in database
            // 5. Log to admin audit for HQ visibility

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                broadcastId = broadcastId,
                confirmed = true,
                confirmedAt = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming broadcast delivery for {BroadcastId}", broadcastId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Service Bus trigger for retrying failed broadcast deliveries
    /// </summary>
    /// <remarks>
    /// Processes failed broadcast deliveries from dead-letter queue.
    /// Retries up to 3 times with exponential backoff (1m, 5m, 15m).
    /// Falls back to SMS if push notifications consistently fail.
    /// </remarks>
    [Function("RetryFailedBroadcasts")]
    [ServiceBusOutput("notification-queue", Connection = "ServiceBusConnection")]
    public async Task<string?> RetryFailedBroadcasts(
        [ServiceBusTrigger("notification-queue/$DeadLetterQueue", Connection = "ServiceBusConnection")] string message,
        FunctionContext context)
    {
        var logger = context.GetLogger("RetryFailedBroadcasts");
        logger.LogInformation("Retrying failed broadcast delivery");

        try
        {
            var failedBroadcast = JsonSerializer.Deserialize<dynamic>(message);

            // TODO: Implement retry logic
            // 1. Parse failed broadcast message
            // 2. Check retry count (max 3 attempts)
            // 3. If under retry limit, re-queue with exponential backoff
            // 4. If retry limit exceeded, fall back to SMS
            // 5. Log failed delivery to admin audit
            // 6. Alert HQ if critical broadcast failed to deliver

            logger.LogInformation("Failed broadcast retry processed");
            
            // Return null if not re-queuing
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrying failed broadcast");
            throw; // Let Service Bus handle DLQ
        }
    }

    /// <summary>
    /// Timer trigger to monitor pending broadcast confirmations
    /// </summary>
    /// <remarks>
    /// Runs every minute.
    /// Identifies broadcasts sent >5 minutes ago without confirmation.
    /// Alerts HQ for critical broadcasts that failed to deliver.
    /// Triggers automatic SMS fallback for unconfirmed deliveries.
    /// </remarks>
    [Function("MonitorBroadcastDeliveries")]
    public async Task MonitorBroadcastDeliveries(
        [TimerTrigger("0 */1 * * * *")] TimerInfo timer,
        FunctionContext context)
    {
        var logger = context.GetLogger("MonitorBroadcastDeliveries");
        logger.LogInformation("Monitoring broadcast delivery confirmations");

        try
        {
            // TODO: Implement delivery monitoring
            // 1. Query broadcasts sent >5 minutes ago without confirmation
            // 2. For each unconfirmed broadcast:
            //    - Check if responder is still online
            //    - If offline, trigger SMS fallback
            //    - If critical severity, alert HQ
            //    - Log monitoring event
            // 3. Update delivery status tracking

            logger.LogInformation("Broadcast delivery monitoring complete");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error monitoring broadcast deliveries");
            // Don't throw - allow next timer execution
        }
    }

    /// <summary>
    /// POST /hq/incident/{incidentId}/command - Send specific command to responders
    /// </summary>
    /// <remarks>
    /// Predefined safety commands for common scenarios:
    /// - RETREAT: Responders should immediately withdraw
    /// - STAND_DOWN: Situation resolved, responders can disengage
    /// - WEAPON_SPOTTED: Weapon present, extreme caution
    /// - POLICE_ARRIVING: Law enforcement en route
    /// - MEDICAL_NEEDED: Request medical assistance
    /// </remarks>
    [Function("SendIncidentCommand")]
    public async Task<SendIncidentCommandResult> SendIncidentCommand(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/incident/{incidentId}/command")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Sending incident command: {IncidentId}", incidentId);

        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var commandRequest = JsonSerializer.Deserialize<IncidentCommandRequest>(requestBody);

            if (commandRequest == null || string.IsNullOrEmpty(commandRequest.Command))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid command request" });
                return new SendIncidentCommandResult { HttpResponse = badRequest };
            }

            // TODO: Implement incident command logic
            // 1. Validate HQ/Admin authentication
            // 2. Validate command is from predefined list
            // 3. Generate appropriate message based on command
            // 4. Send as HQ broadcast with critical priority
            // 5. Log command to incident timeline
            // 6. Return command ID and delivery status

            var commandId = Guid.NewGuid().ToString();
            var message = GetCommandMessage(commandRequest.Command);

            // Broadcast via SignalR
            var signalRMessage = new SignalRMessageAction("incidentCommand")
            {
                GroupName = $"incident-{incidentId}",
                Arguments = new object[]
                {
                    new
                    {
                        commandId = commandId,
                        command = commandRequest.Command,
                        message = message,
                        severity = "critical",
                        timestamp = DateTime.UtcNow
                    }
                }
            };

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                commandId = commandId,
                command = commandRequest.Command,
                message = message,
                status = "queued",
                recipientCount = 2 // Placeholder
            });

            return new SendIncidentCommandResult
            {
                HttpResponse = response,
                SignalRMessages = new[] { signalRMessage }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending incident command for {IncidentId}", incidentId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return new SendIncidentCommandResult { HttpResponse = errorResponse };
        }
    }

    /// <summary>
    /// Helper method to generate message from command type
    /// </summary>
    private string GetCommandMessage(string command)
    {
        return command.ToUpperInvariant() switch
        {
            "RETREAT" => "RETREAT IMMEDIATELY - HQ OVERRIDE",
            "STAND_DOWN" => "STAND DOWN - SITUATION RESOLVED",
            "WEAPON_SPOTTED" => "WEAPON SPOTTED - EXTREME CAUTION",
            "POLICE_ARRIVING" => "POLICE ARRIVING IN 2 MINUTES",
            "MEDICAL_NEEDED" => "MEDICAL EMERGENCY - PARAMEDICS DISPATCHED",
            _ => $"HQ COMMAND: {command}"
        };
    }
}

/// <summary>
/// Result type for SendHqBroadcast function with multiple outputs
/// </summary>
public class SendHqBroadcastResult
{
    [HttpResult]
    public HttpResponseData? HttpResponse { get; set; }
    
    [SignalROutput(HubName = "watchHub", ConnectionStringSetting = "AzureSignalRConnectionString")]
    public SignalRMessageAction[]? SignalRMessages { get; set; }
}

/// <summary>
/// Result type for SendIncidentCommand function with multiple outputs
/// </summary>
public class SendIncidentCommandResult
{
    [HttpResult]
    public HttpResponseData? HttpResponse { get; set; }
    
    [SignalROutput(HubName = "watchHub", ConnectionStringSetting = "AzureSignalRConnectionString")]
    public SignalRMessageAction[]? SignalRMessages { get; set; }
}

/// <summary>
/// Request model for HQ broadcast
/// </summary>
public class HqBroadcastRequest
{
    public string IncidentId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Severity { get; set; } = "critical";
    public bool IncludeAudioTts { get; set; } = true;
    public string? AudioClipUrl { get; set; }
    public bool SmsFallback { get; set; } = true;
}

/// <summary>
/// Request model for incident command
/// </summary>
public class IncidentCommandRequest
{
    public string Command { get; set; } = string.Empty;
}
