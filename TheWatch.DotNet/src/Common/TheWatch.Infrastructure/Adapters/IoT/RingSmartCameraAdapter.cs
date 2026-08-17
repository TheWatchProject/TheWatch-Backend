using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Adapters.IoT;

public sealed record RingCameraDevice(string CameraId, string Name, double Latitude, double Longitude, bool IsOnline);
public sealed record RingFootageClip(string ClipId, string CameraId, double Latitude, double Longitude, DateTime CapturedAt, string BlobUri, string Sha256Hash);
public sealed record RingFootageRequest(string IncidentId, double Latitude, double Longitude, double RadiusMeters, DateTime WindowStart, DateTime WindowEnd);
public sealed record RingSubmissionResult(bool Accepted, string ClipId, string IncidentId, string? TrackingHandle, string? RejectionReason);

public interface IRingSmartCameraAdapter
{
    Task<IReadOnlyList<RingCameraDevice>> FindCamerasInRadiusAsync(double lat, double lon, double radiusMeters, CancellationToken ct = default);
    Task<RingSubmissionResult> SubmitClipEvidenceAsync(RingFootageRequest request, RingFootageClip clip, CancellationToken ct = default);
}

/// <summary>
/// Ring IoT Smart Camera Directory and Incident Footage Ingestion Adapter.
/// </summary>
public sealed class RingSmartCameraAdapter : IRingSmartCameraAdapter
{
    private readonly ILogger<RingSmartCameraAdapter> _logger;
    private readonly List<RingCameraDevice> _registeredCameras = new();
    private const double EarthRadiusMeters = 6_371_000.0;

    public RingSmartCameraAdapter(ILogger<RingSmartCameraAdapter> logger)
    {
        _logger = logger;
    }

    public void RegisterCamera(RingCameraDevice camera) => _registeredCameras.Add(camera);

    public Task<IReadOnlyList<RingCameraDevice>> FindCamerasInRadiusAsync(double lat, double lon, double radiusMeters, CancellationToken ct = default)
    {
        var eligible = _registeredCameras
            .Where(c => c.IsOnline && HaversineMeters(lat, lon, c.Latitude, c.Longitude) <= radiusMeters)
            .ToList();

        _logger.LogInformation("Discovered {Count} Ring cameras within {Radius}m radius", eligible.Count, radiusMeters);
        return Task.FromResult<IReadOnlyList<RingCameraDevice>>(eligible);
    }

    public Task<RingSubmissionResult> SubmitClipEvidenceAsync(RingFootageRequest request, RingFootageClip clip, CancellationToken ct = default)
    {
        if (clip.CapturedAt < request.WindowStart || clip.CapturedAt > request.WindowEnd)
        {
            return Task.FromResult(new RingSubmissionResult(false, clip.ClipId, request.IncidentId, null, "Clip outside requested incident time window"));
        }

        var dist = HaversineMeters(request.Latitude, request.Longitude, clip.Latitude, clip.Longitude);
        if (dist > request.RadiusMeters)
        {
            return Task.FromResult(new RingSubmissionResult(false, clip.ClipId, request.IncidentId, null, $"Camera distance {dist:F0}m exceeds requested radius {request.RadiusMeters:F0}m"));
        }

        string trackingHandle = $"RING-CUSTODY-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        _logger.LogInformation("Ring clip {ClipId} accepted into evidence vault under handle {Handle}", clip.ClipId, trackingHandle);

        return Task.FromResult(new RingSubmissionResult(true, clip.ClipId, request.IncidentId, trackingHandle, null));
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusMeters * (2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }
}
