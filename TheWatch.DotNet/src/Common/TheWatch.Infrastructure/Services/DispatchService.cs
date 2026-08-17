using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service implementation for dispatching responders to incidents.
/// Uses Azure Service Bus for reliable message delivery.
/// </summary>
public class DispatchService : IDispatchService
{
    private readonly ServiceBusSender _dispatchQueueSender;
    private readonly ILogger<DispatchService> _logger;
    private readonly GeohashService _geohashService;

    public DispatchService(
        ServiceBusClient serviceBusClient,
        ILogger<DispatchService> logger)
    {
        _dispatchQueueSender = serviceBusClient.CreateSender("dispatch-queue");
        _logger = logger;
        _geohashService = new GeohashService();
    }

    public async Task DispatchRespondersAsync(Guid incidentId, double latitude, double longitude,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Dispatching responders to incident {IncidentId} at ({Lat}, {Lng})",
            incidentId, latitude, longitude);

        var geohash = _geohashService.Encode(latitude, longitude, 9);

        // Queue dispatch message for processing
        var message = new ServiceBusMessage(JsonSerializer.Serialize(new
        {
            Type = "DISPATCH_REQUEST",
            IncidentId = incidentId,
            Latitude = latitude,
            Longitude = longitude,
            Geohash = geohash,
            Timestamp = DateTime.UtcNow
        }));

        message.ApplicationProperties["incidentId"] = incidentId.ToString();
        message.ApplicationProperties["action"] = "dispatch";

        await _dispatchQueueSender.SendMessageAsync(message, cancellationToken);

        _logger.LogInformation("Dispatch request queued for incident {IncidentId}", incidentId);
    }

    public async Task<IEnumerable<Guid>> FindNearbyRespondersAsync(string geohashPrefix, int maxResponders,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Finding up to {MaxResponders} responders near geohash prefix {GeohashPrefix}",
            maxResponders, geohashPrefix);

        // In a full implementation, this would query the location service
        // For now, return empty - the actual dispatch is handled by Service Bus consumers
        await Task.CompletedTask;

        return Enumerable.Empty<Guid>();
    }

    public async Task EscalateToPoliceSilentlyAsync(Guid incidentId, Guid summonerId, CancellationToken cancellationToken = default)
    {
        // CRITICAL: Use neutral language in logs - never mention "duress"
        _logger.LogWarning("Escalating incident {IncidentId} to priority emergency response", incidentId);

        // Queue high-priority escalation message
        var message = new ServiceBusMessage(JsonSerializer.Serialize(new
        {
            Type = "PRIORITY_ESCALATION",
            IncidentId = incidentId,
            SummonerId = summonerId,
            EscalationType = "police_and_hq",
            Priority = "critical",
            SilentMode = true, // Do not alert summoner
            Timestamp = DateTime.UtcNow
        }));

        message.ApplicationProperties["incidentId"] = incidentId.ToString();
        message.ApplicationProperties["action"] = "escalate_priority";
        message.ApplicationProperties["priority"] = "critical";

        await _dispatchQueueSender.SendMessageAsync(message, cancellationToken);

        _logger.LogInformation("Priority escalation queued for incident {IncidentId}", incidentId);
    }
}
