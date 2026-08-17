using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace TheWatch.MobileBff;

/// <summary>
/// Real-time SignalR hub delivering low-latency incident updates, responder GPS streams, and drone video telemetry.
/// </summary>
public class EmergencyRealTimeHub : Hub
{
    private readonly ILogger<EmergencyRealTimeHub> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="EmergencyRealTimeHub"/>.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public EmergencyRealTimeHub(ILogger<EmergencyRealTimeHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribes the client to live updates for a specific emergency incident.
    /// </summary>
    /// <param name="incidentId">The incident identifier.</param>
    public async Task SubscribeToIncident(string incidentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"incident-{incidentId}");
        _logger.LogInformation("Connection {ConnectionId} subscribed to incident {IncidentId}", Context.ConnectionId, incidentId);
    }

    /// <summary>
    /// Broadcasts live GPS telemetry coordinates for a responder unit.
    /// </summary>
    /// <param name="responderId">Responder unit ID.</param>
    /// <param name="lat">Latitude coordinate.</param>
    /// <param name="lon">Longitude coordinate.</param>
    public async Task BroadcastResponderLocation(string responderId, double lat, double lon)
    {
        await Clients.All.SendAsync("ResponderLocationUpdated", responderId, lat, lon);
    }
}
