using Microsoft.AspNetCore.SignalR;
using TheWatch.Contracts;

namespace TheWatch.MobileBff.Hubs;

/// <summary>
/// Real-time SignalR Hub for high-frequency GPS telemetry streaming (10Hz), geofences, falls, and duress events.
/// </summary>
public sealed class TelemetryHub : Hub<HubContracts.ITelemetryClient>
{
    private readonly ILogger<TelemetryHub> _logger;

    public TelemetryHub(ILogger<TelemetryHub> logger)
    {
        _logger = logger;
    }

    public async Task StreamLocationPing(TelemetryContracts.LocationPing ping)
    {
        await Clients.Others.OnLocationUpdated(ping);
    }

    public async Task NotifyGeofenceBreach(TelemetryContracts.GeofenceBreachAlert breach)
    {
        _logger.LogWarning("Geofence breach detected: {GeofenceName} by subject {SubjectId}", breach.GeofenceName, breach.SubjectId);
        await Clients.All.OnGeofenceBreach(breach);
    }

    public async Task BroadcastFallEvent(string userId, double peakG, double lat, double lon)
    {
        _logger.LogCritical("MAN-DOWN / FALL DETECTED for user {UserId}: Peak {PeakG:F1}g at ({Lat}, {Lon})", userId, peakG, lat, lon);
        await Clients.All.OnFallDetected(userId, peakG, lat, lon);
    }

    public async Task BroadcastVoicePanicMatch(string userId, string phrase, double confidenceScore)
    {
        _logger.LogCritical("ACOUSTIC PANIC PHRASE MATCHED: User {UserId} spoke '{Phrase}' (Confidence {Conf:P1})", userId, phrase, confidenceScore);
        await Clients.All.OnAcousticPhraseTriggered(userId, phrase, confidenceScore);
    }

    public async Task BroadcastCovertDuress(string userId, double lat, double lon)
    {
        _logger.LogCritical("COVERT DURESS TAP CADENCE TRIGGERED: User {UserId} at ({Lat}, {Lon})", userId, lat, lon);
        await Clients.All.OnCovertDuressSignal(userId, lat, lon);
    }

    public async Task NotifyEvidenceUploaded(EvidenceAndResponderContracts.ForensicEvidenceItem evidence)
    {
        _logger.LogInformation("Forensic Evidence uploaded: {EvidenceId} ({MimeType}) SHA-256: {Sha256}", evidence.EvidenceId, evidence.ContentMimeType, evidence.Sha256ChecksumHex);
        await Clients.All.OnEvidenceUploaded(evidence);
    }

    public async Task BroadcastHazardRoute(MappingAndRoutingContracts.EmergencyRoutePlan route)
    {
        _logger.LogInformation("Hazard-avoidance route updated for unit {UnitId} on incident {IncidentId}", route.AssignedUnitId, route.IncidentId);
        await Clients.All.OnHazardRouteUpdated(route);
    }
}
