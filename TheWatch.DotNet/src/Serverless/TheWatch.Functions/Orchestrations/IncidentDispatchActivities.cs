using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Functions.Orchestrations;

/// <summary>
/// Durable Functions activity triggers for the Incident Dispatch saga.
/// </summary>
public class IncidentDispatchActivities
{
    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<IncidentDispatchActivities> _logger;

    public IncidentDispatchActivities(
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        INotificationService notificationService,
        ILogger<IncidentDispatchActivities> logger)
    {
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    [Function(nameof(FindNearbyRespondersActivity))]
    public async Task<List<Guid>> FindNearbyRespondersActivity(
        [ActivityTrigger] FindNearbyRespondersInput input)
    {
        _logger.LogInformation("Finding nearby responders for geohash {Geohash}", input.Geohash);

        var availableResponders = await _userRepository.GetRespondersByStatusAsync("available");
        var responderIds = availableResponders
            .Take(input.MaxResponders > 0 ? input.MaxResponders : 20)
            .Select(u => u.UserId)
            .ToList();

        return responderIds;
    }

    [Function(nameof(SendResponderNotificationActivity))]
    public async Task SendResponderNotificationActivity(
        [ActivityTrigger] ResponderNotificationInput input)
    {
        _logger.LogInformation("Sending dispatch notification to responder {ResponderId} for incident {IncidentId}",
            input.ResponderId, input.IncidentId);

        await _notificationService.SendDispatchNotificationAsync(input.ResponderId, input.IncidentId);
    }

    [Function(nameof(CancelIncidentNotificationsActivity))]
    public Task CancelIncidentNotificationsActivity(
        [ActivityTrigger] Guid incidentId)
    {
        _logger.LogInformation("Cancelling pending notifications for incident {IncidentId}", incidentId);
        return Task.CompletedTask;
    }

    [Function(nameof(AssignResponderActivity))]
    public async Task AssignResponderActivity(
        [ActivityTrigger] AssignResponderInput input)
    {
        _logger.LogInformation("Assigning responder {ResponderId} as {Role} for incident {IncidentId}",
            input.ResponderId, input.Role, input.IncidentId);

        var incident = await _incidentRepository.GetByIdAsync(input.IncidentId);
        if (incident != null)
        {
            var assignment = new ResponderAssignment
            {
                AssignmentId = Guid.NewGuid(),
                IncidentId = input.IncidentId,
                ResponderId = input.ResponderId,
                Role = input.Role,
                Status = "accepted",
                AssignedAt = DateTime.UtcNow,
                AcceptedAt = DateTime.UtcNow
            };

            incident.ResponderAssignments.Add(assignment);
            incident.Status = input.Role == "First" ? "responder_assigned" : incident.Status;
            await _incidentRepository.UpdateAsync(incident);
        }
    }

    [Function(nameof(EscalateToEmergencyServicesActivity))]
    public async Task EscalateToEmergencyServicesActivity(
        [ActivityTrigger] EscalateInput input)
    {
        _logger.LogWarning("Escalating incident {IncidentId} to emergency services (911). Reason: {Reason}",
            input.IncidentId, input.Reason);

        var incident = await _incidentRepository.GetByIdAsync(input.IncidentId);
        if (incident != null)
        {
            incident.Status = "escalated_to_emergency_services";
            incident.EscalatedToPolice = true;
            incident.UpdatedAt = DateTime.UtcNow;
            await _incidentRepository.UpdateAsync(incident);
        }

        await _notificationService.SendHqAlertAsync(
            $"911 ESCALATION - Incident {input.IncidentId}",
            input.Reason,
            "critical");
    }

    [Function(nameof(UpdateIncidentStatusActivity))]
    public async Task UpdateIncidentStatusActivity(
        [ActivityTrigger] UpdateIncidentStatusInput input)
    {
        _logger.LogInformation("Updating incident {IncidentId} status to {NewStatus}",
            input.IncidentId, input.NewStatus);

        var incident = await _incidentRepository.GetByIdAsync(input.IncidentId);
        if (incident != null)
        {
            incident.Status = input.NewStatus;
            incident.UpdatedAt = DateTime.UtcNow;
            await _incidentRepository.UpdateAsync(incident);
        }
    }

    [Function(nameof(RollbackIncidentDispatchActivity))]
    public async Task RollbackIncidentDispatchActivity(
        [ActivityTrigger] Guid incidentId)
    {
        _logger.LogWarning("Rolling back incident dispatch for incident {IncidentId}", incidentId);

        var incident = await _incidentRepository.GetByIdAsync(incidentId);
        if (incident != null)
        {
            incident.Status = "dispatch_failed";
            incident.UpdatedAt = DateTime.UtcNow;
            await _incidentRepository.UpdateAsync(incident);
        }
    }
}
