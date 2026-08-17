using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;
using static TheWatch.Contracts.VolunteerCrowdMonitoringContracts;

namespace TheWatch.Geospatial.Db;

public interface IVolunteerCrowdMonitoringEngine
{
    void CreateEvent(CrowdSafetyMonitoringEvent safetyEvent);
    IReadOnlyList<CrowdSafetyMonitoringEvent> GetActiveEvents();
    VolunteerMonitoringSession JoinEvent(string eventId, string userId, string handle, double lat, double lon);
    TriangulatedCrowdDistressSignal? IngestVolunteerDetection(
        string eventId,
        string volunteerUserId,
        string detectedPhrase,
        double confidence,
        double volunteerLat,
        double volunteerLon,
        DateTime detectionTimeUtc);
}

public sealed record PendingCrowdDetection(
    string VolunteerUserId,
    string DetectedPhrase,
    double Confidence,
    double Lat,
    double Lon,
    DateTime DetectionTimeUtc
);

/// <summary>
/// Distributed Volunteer Crowd Phrase Monitoring & Spatial Triangulation Engine.
/// </summary>
public sealed class VolunteerCrowdMonitoringEngine : IVolunteerCrowdMonitoringEngine
{
    private readonly ConcurrentDictionary<string, CrowdSafetyMonitoringEvent> _events = new();
    private readonly ConcurrentDictionary<string, List<VolunteerMonitoringSession>> _sessions = new();
    private readonly ConcurrentDictionary<string, List<PendingCrowdDetection>> _pendingDetections = new();

    public VolunteerCrowdMonitoringEngine()
    {
        SeedSampleEvent();
    }

    private void SeedSampleEvent()
    {
        var sampleEvent = new CrowdSafetyMonitoringEvent(
            "EVENT-FESTIVAL-01",
            "Golden Gate Summer Music Festival 2026",
            "SF Emergency Management Event Safety Unit",
            37.7690,
            -122.4835,
            CoverageRadiusMeters: 800.0,
            new List<string> { "Active Shooter", "Fire", "Medical Emergency", "Stampede", "Help" },
            TargetVolunteerCount: 50,
            CurrentActiveVolunteers: 12,
            EventMonitoringStatus.MonitoringLive,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddHours(5)
        );

        _events.TryAdd(sampleEvent.EventId, sampleEvent);
    }

    public void CreateEvent(CrowdSafetyMonitoringEvent safetyEvent) => _events[safetyEvent.EventId] = safetyEvent;

    public IReadOnlyList<CrowdSafetyMonitoringEvent> GetActiveEvents() => _events.Values.ToList();

    public VolunteerMonitoringSession JoinEvent(string eventId, string userId, string handle, double lat, double lon)
    {
        var session = new VolunteerMonitoringSession(
            $"SESSION-VOL-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            eventId,
            userId,
            handle,
            lat,
            lon,
            BatteryLevelPercent: 92.0,
            IsAcousticScannerActive: true,
            DateTime.UtcNow
        );

        _sessions.AddOrUpdate(
            eventId,
            new List<VolunteerMonitoringSession> { session },
            (_, list) => { lock (list) { list.Add(session); return list; } }
        );

        if (_events.TryGetValue(eventId, out var ev))
        {
            _events[eventId] = ev with { CurrentActiveVolunteers = ev.CurrentActiveVolunteers + 1 };
        }

        return session;
    }

    public TriangulatedCrowdDistressSignal? IngestVolunteerDetection(
        string eventId,
        string volunteerUserId,
        string detectedPhrase,
        double confidence,
        double volunteerLat,
        double volunteerLon,
        DateTime detectionTimeUtc)
    {
        var detection = new PendingCrowdDetection(volunteerUserId, detectedPhrase, confidence, volunteerLat, volunteerLon, detectionTimeUtc);

        var list = _pendingDetections.GetOrAdd(eventId, _ => new List<PendingCrowdDetection>());
        lock (list)
        {
            list.Add(detection);

            // Prune detections older than 4 seconds
            list.RemoveAll(d => (detectionTimeUtc - d.DetectionTimeUtc).TotalSeconds > 4.0);

            // Find matching phrases reported by distinct volunteers
            var matches = list
                .Where(d => d.DetectedPhrase.Equals(detectedPhrase, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var distinctVolunteers = matches.Select(m => m.VolunteerUserId).Distinct().ToList();

            if (distinctVolunteers.Count >= 2)
            {
                // Multi-device consensus reached! Triangulate weighted centroid
                double totalWeight = matches.Sum(m => m.Confidence);
                double weightedLat = matches.Sum(m => m.Lat * m.Confidence) / totalWeight;
                double weightedLon = matches.Sum(m => m.Lon * m.Confidence) / totalWeight;
                double avgConfidence = matches.Average(m => m.Confidence);

                return new TriangulatedCrowdDistressSignal(
                    $"TRIANGULATED-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
                    eventId,
                    detectedPhrase,
                    distinctVolunteers.Count,
                    weightedLat,
                    weightedLon,
                    ConfidenceScore: Math.Min(0.99, avgConfidence + 0.15), // elevated consensus confidence
                    EstimatedAcousticRadiusMeters: 45.0,
                    matches.Min(m => m.DetectionTimeUtc)
                );
            }
        }

        return null;
    }
}
