using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace TheWatch.Functions.Orchestrations;

/// <summary>
/// Durable Functions orchestration for incident dispatch saga.
/// Coordinates: Incident creation → Responder query → Notification → Assignment
/// Implements compensation logic for rollback on failures.
/// </summary>
public class IncidentDispatchOrchestration
{
    [Function(nameof(IncidentDispatchOrchestrator))]
    public static async Task<IncidentDispatchResult> IncidentDispatchOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<IncidentDispatchInput>() ?? throw new ArgumentNullException("IncidentDispatchInput");
        var logger = context.CreateReplaySafeLogger(nameof(IncidentDispatchOrchestrator));

        logger.LogInformation("Starting incident dispatch orchestration for incident {IncidentId}", input.IncidentId);

        var result = new IncidentDispatchResult
        {
            IncidentId = input.IncidentId,
            Success = false
        };

        try
        {
            // Step 1: Find nearby responders
            logger.LogInformation("Step 1: Finding nearby responders");
            var nearbyResponders = await context.CallActivityAsync<List<Guid>>(
                nameof(IncidentDispatchActivities.FindNearbyRespondersActivity),
                new FindNearbyRespondersInput
                {
                    Geohash = input.Geohash,
                    MaxResponders = 20,
                    RequiredRoles = new[] { "responder" }
                });

            if (!nearbyResponders.Any())
            {
                logger.LogWarning("No nearby responders found for incident {IncidentId}", input.IncidentId);
                result.ErrorMessage = "No nearby responders available";
                
                // Escalate if no responders found
                if (input.AutoEscalateIfNoResponders)
                {
                    await context.CallActivityAsync(
                        nameof(IncidentDispatchActivities.EscalateToEmergencyServicesActivity),
                        new EscalateInput { IncidentId = input.IncidentId, Reason = "No nearby responders" });
                    result.Escalated = true;
                }
                
                return result;
            }

            result.RespondersNotified = nearbyResponders.Count;

            // Step 2: Send notifications to responders
            logger.LogInformation("Step 2: Notifying {Count} responders", nearbyResponders.Count);
            var notificationTasks = nearbyResponders.Select(responderId =>
                context.CallActivityAsync(
                    nameof(IncidentDispatchActivities.SendResponderNotificationActivity),
                    new ResponderNotificationInput
                    {
                        ResponderId = responderId,
                        IncidentId = input.IncidentId,
                        Priority = input.Priority
                    })).ToList();

            await Task.WhenAll(notificationTasks);

            // Step 3: Wait for responder acceptance (with timeout)
            logger.LogInformation("Step 3: Waiting for responder acceptance");
            var acceptanceTimeout = TimeSpan.FromMinutes(2);
            var acceptanceEvent = context.WaitForExternalEvent<ResponderAcceptanceEvent>("ResponderAccepted");
            var timeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(acceptanceTimeout), CancellationToken.None);

            var winner = await Task.WhenAny(acceptanceEvent, timeoutTask);

            if (winner == timeoutTask)
            {
                logger.LogWarning("Timeout waiting for responder acceptance for incident {IncidentId}", input.IncidentId);
                result.ErrorMessage = "Timeout waiting for responder acceptance";
                
                // Compensation: Cancel notifications
                await context.CallActivityAsync(
                    nameof(IncidentDispatchActivities.CancelIncidentNotificationsActivity),
                    input.IncidentId);

                // Auto-escalate on timeout
                if (input.AutoEscalateOnTimeout)
                {
                    await context.CallActivityAsync(
                        nameof(IncidentDispatchActivities.EscalateToEmergencyServicesActivity),
                        new EscalateInput { IncidentId = input.IncidentId, Reason = "Timeout waiting for responders" });
                    result.Escalated = true;
                }
                
                return result;
            }

            var acceptance = await acceptanceEvent;
            result.FirstResponderId = acceptance.ResponderId;

            // Step 4: Assign first responder
            logger.LogInformation("Step 4: Assigning first responder {ResponderId}", acceptance.ResponderId);
            await context.CallActivityAsync(
                nameof(IncidentDispatchActivities.AssignResponderActivity),
                new AssignResponderInput
                {
                    IncidentId = input.IncidentId,
                    ResponderId = acceptance.ResponderId,
                    Role = "First"
                });

            // Step 5: Wait for second responder (optional, shorter timeout)
            logger.LogInformation("Step 5: Waiting for second responder");
            var secondAcceptanceTimeout = TimeSpan.FromMinutes(1);
            var secondAcceptanceEvent = context.WaitForExternalEvent<ResponderAcceptanceEvent>("SecondResponderAccepted");
            var secondTimeoutTask = context.CreateTimer(context.CurrentUtcDateTime.Add(secondAcceptanceTimeout), CancellationToken.None);

            var secondWinner = await Task.WhenAny(secondAcceptanceEvent, secondTimeoutTask);

            if (secondWinner != secondTimeoutTask)
            {
                var secondAcceptance = await secondAcceptanceEvent;
                result.SecondResponderId = secondAcceptance.ResponderId;

                await context.CallActivityAsync(
                    nameof(IncidentDispatchActivities.AssignResponderActivity),
                    new AssignResponderInput
                    {
                        IncidentId = input.IncidentId,
                        ResponderId = secondAcceptance.ResponderId,
                        Role = "Second"
                    });
            }

            // Step 6: Update incident status to dispatched
            await context.CallActivityAsync(
                nameof(IncidentDispatchActivities.UpdateIncidentStatusActivity),
                new UpdateIncidentStatusInput
                {
                    IncidentId = input.IncidentId,
                    NewStatus = "dispatched"
                });

            result.Success = true;
            logger.LogInformation("Incident dispatch orchestration completed successfully for incident {IncidentId}", input.IncidentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in incident dispatch orchestration for incident {IncidentId}", input.IncidentId);
            result.ErrorMessage = ex.Message;

            // Compensation logic
            try
            {
                await context.CallActivityAsync(
                    nameof(IncidentDispatchActivities.RollbackIncidentDispatchActivity),
                    input.IncidentId);
            }
            catch (Exception rollbackEx)
            {
                logger.LogError(rollbackEx, "Error during rollback for incident {IncidentId}", input.IncidentId);
            }
        }

        return result;
    }
}

// Input/Output DTOs for the orchestration
public record IncidentDispatchInput
{
    public Guid IncidentId { get; init; }
    public string Geohash { get; init; } = string.Empty;
    public int Priority { get; init; } = 1;
    public bool AutoEscalateIfNoResponders { get; init; } = true;
    public bool AutoEscalateOnTimeout { get; init; } = true;
}

public record IncidentDispatchResult
{
    public Guid IncidentId { get; init; }
    public bool Success { get; set; }
    public Guid? FirstResponderId { get; set; }
    public Guid? SecondResponderId { get; set; }
    public int RespondersNotified { get; set; }
    public bool Escalated { get; set; }
    public string? ErrorMessage { get; set; }
}

public record ResponderAcceptanceEvent
{
    public Guid ResponderId { get; init; }
    public DateTime AcceptedAt { get; init; }
}

public record FindNearbyRespondersInput
{
    public string Geohash { get; init; } = string.Empty;
    public int MaxResponders { get; init; }
    public string[] RequiredRoles { get; init; } = Array.Empty<string>();
}

public record ResponderNotificationInput
{
    public Guid ResponderId { get; init; }
    public Guid IncidentId { get; init; }
    public int Priority { get; init; }
}

public record AssignResponderInput
{
    public Guid IncidentId { get; init; }
    public Guid ResponderId { get; init; }
    public string Role { get; init; } = string.Empty;
}

public record UpdateIncidentStatusInput
{
    public Guid IncidentId { get; init; }
    public string NewStatus { get; init; } = string.Empty;
}

public record EscalateInput
{
    public Guid IncidentId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
