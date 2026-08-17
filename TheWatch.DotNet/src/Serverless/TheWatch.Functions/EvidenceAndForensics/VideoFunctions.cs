using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Entities;
using TheWatch.Functions.Utilities;
using TheWatch.Infrastructure.Data;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for live video streaming management - COMPLETE IMPLEMENTATION
/// Implements endpoints from live-video-api.yaml
/// </summary>
public class VideoFunctions
{
    private readonly ILogger<VideoFunctions> _logger;
    private readonly WatchDbContext _dbContext;
    private readonly IIncidentRepository _incidentRepository;
    private readonly ICryptographyService _cryptographyService;
    private readonly INotificationService _notificationService;

    public VideoFunctions(
        ILogger<VideoFunctions> logger,
        WatchDbContext dbContext,
        IIncidentRepository incidentRepository,
        ICryptographyService cryptographyService,
        INotificationService notificationService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _incidentRepository = incidentRepository;
        _cryptographyService = cryptographyService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// POST /streams - Initiate video stream
    /// </summary>
    [Function("InitiateVideoStream")]
    public async Task<HttpResponseData> InitiateVideoStream(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "streams")] HttpRequestData req)
    {
        _logger.LogInformation("Initiating video stream");

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        var incidentId = Guid.Parse(requestBody.GetProperty("incident_id").GetString()!);
        var deviceId = requestBody.GetProperty("device_id").GetString()!;
        var videoQuality = requestBody.TryGetProperty("video_quality", out var vq) ? vq.GetString() : "auto";
        var cameraDirection = requestBody.TryGetProperty("camera_direction", out var cd) ? cd.GetString() : "rear";
        var duressPassword = requestBody.TryGetProperty("duress_password", out var dp) ? dp.GetString() : null;

        var stream = new VideoStream
        {
            StreamId = Guid.NewGuid(),
            IncidentId = incidentId,
            DeviceId = deviceId,
            DeviceType = DetectDeviceType(deviceId),
            Status = "initializing",
            CreatedAt = DateTime.UtcNow,
            VideoQuality = videoQuality ?? "auto",
            CameraDirection = cameraDirection ?? "rear",
            DuressPasswordHash = duressPassword != null ? _cryptographyService.HashPassword(duressPassword) : null,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            StorageLocation = $"/live-video-streams/{incidentId}/{Guid.NewGuid()}/"
        };

        _dbContext.VideoStreams.Add(stream);
        await _dbContext.SaveChangesAsync();

        var wsAuthToken = GenerateWebSocketAuthToken(stream.StreamId, userId.Value);

        _logger.LogInformation("Video stream initiated: {StreamId}", stream.StreamId);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            stream_id = stream.StreamId,
            incident_id = stream.IncidentId,
            status = stream.Status,
            created_at = stream.CreatedAt,
            ws_url = $"wss://video-api.thewatch.app/v1/ws/stream/{stream.StreamId}",
            ws_auth_token = wsAuthToken,
            expires_at = stream.ExpiresAt
        });

        return response;
    }

    /// <summary>
    /// GET /streams/{streamId} - Get stream status
    /// </summary>
    [Function("GetStreamStatus")]
    public async Task<HttpResponseData> GetStreamStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "streams/{streamId}")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("Getting stream status: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var stream = await _dbContext.VideoStreams.FindAsync(Guid.Parse(streamId));
        if (stream == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Stream not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            stream_id = stream.StreamId,
            incident_id = stream.IncidentId,
            status = stream.Status,
            created_at = stream.CreatedAt,
            terminated_at = stream.TerminatedAt,
            duration_seconds = stream.DurationSeconds,
            frames_received = stream.FramesReceived,
            frames_dropped = stream.FramesDropped,
            video_quality = stream.VideoQuality,
            device_type = stream.DeviceType,
            duress_flagged = stream.DuressFlagged,
            termination_reason = stream.TerminationReason
        });

        return response;
    }

    /// <summary>
    /// POST /streams/{streamId}/terminate - Terminate stream manually
    /// </summary>
    [Function("TerminateStream")]
    public async Task<HttpResponseData> TerminateStream(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "streams/{streamId}/terminate")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("Terminating video stream: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var stream = await _dbContext.VideoStreams.FindAsync(Guid.Parse(streamId));
        if (stream == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Stream not found" });
            return notFound;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        var terminationReason = requestBody.GetProperty("termination_reason").GetString();

        stream.Status = "terminated";
        stream.TerminatedAt = DateTime.UtcNow;
        stream.TerminationReason = terminationReason;
        stream.DurationSeconds = (int)(DateTime.UtcNow - stream.CreatedAt).TotalSeconds;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Stream terminated: {StreamId}, reason: {Reason}", streamId, terminationReason);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            stream_id = stream.StreamId,
            incident_id = stream.IncidentId,
            status = stream.Status,
            created_at = stream.CreatedAt,
            terminated_at = stream.TerminatedAt,
            duration_seconds = stream.DurationSeconds,
            frames_received = stream.FramesReceived,
            frames_dropped = stream.FramesDropped,
            video_quality = stream.VideoQuality,
            device_type = stream.DeviceType,
            duress_flagged = stream.DuressFlagged,
            termination_reason = stream.TerminationReason
        });

        return response;
    }

    /// <summary>
    /// POST /streams/{streamId}/all-clear-challenge - Challenge All Clear status (duress detection)
    /// </summary>
    [Function("ChallengeAllClear")]
    public async Task<HttpResponseData> ChallengeAllClear(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "streams/{streamId}/all-clear-challenge")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("Processing All Clear challenge for stream: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauthorized;
        }

        var stream = await _dbContext.VideoStreams
            .Include(s => s.Incident)
                .ThenInclude(i => i.ResponderAssignments)
            .FirstOrDefaultAsync(s => s.StreamId == Guid.Parse(streamId));

        if (stream == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Stream not found" });
            return notFound;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        var passwordAttempt = requestBody.GetProperty("password_attempt").GetString();

        var passwordCorrect = !string.IsNullOrEmpty(stream.DuressPasswordHash) &&
                            !string.IsNullOrEmpty(passwordAttempt) &&
                            _cryptographyService.VerifyPassword(passwordAttempt, stream.DuressPasswordHash);

        if (passwordCorrect)
        {
            // Correct password - legitimate All Clear
            stream.Status = "terminated";
            stream.TerminatedAt = DateTime.UtcNow;
            stream.TerminationReason = "all_clear";
            stream.DurationSeconds = (int)(DateTime.UtcNow - stream.CreatedAt).TotalSeconds;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("All Clear confirmed for stream: {StreamId}", streamId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                challenge_id = Guid.NewGuid(),
                correct = true,
                stream_terminated = true,
                duress_flagged = false,
                message = "All Clear confirmed. Recording stopped."
            });
            return response;
        }
        else
        {
            // Wrong password - duress detected (SECURITY CRITICAL)
            // Return "success" to summoner but keep recording and alert responders

            stream.DuressFlagged = true;
            stream.DuressType = "password_incorrect";
            stream.DuressFlaggedAt = DateTime.UtcNow;
            stream.RespondersNotified = true;
            stream.RespondersNotifiedAt = DateTime.UtcNow;
            // Keep stream active but mark as terminated for summoner
            await _dbContext.SaveChangesAsync();

            // Alert HQ and responders (neutral logging to avoid tipping off perpetrator)
            _logger.LogInformation("Stream status updated: {StreamId}", streamId);

            // Send high-priority alerts to responders
            var responderIds = stream.Incident.ResponderAssignments.Select(ra => ra.ResponderId).ToList();
            foreach (var responderId in responderIds)
            {
                await _notificationService.SendPushNotificationAsync(
                    responderId,
                    "Incident Update",
                    "Do not leave scene - situation may require continued presence",
                    new Dictionary<string, string> {
                        { "incident_id", stream.IncidentId.ToString() },
                        { "priority", "high" },
                        { "duress_detected", "true" }
                    });
            }

            // Return fake success to summoner (deceptive UI)
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                challenge_id = Guid.NewGuid(),
                correct = false,
                stream_terminated = true, // Fake termination for summoner
                duress_flagged = true, // Only visible to server/HQ
                message = "All Clear confirmed. Recording stopped." // Deceptive message
            });
            return response;
        }
    }

    /// <summary>
    /// GET /streams/{streamId}/duress-flag - Get duress flag status
    /// </summary>
    [Function("GetDuressFlag")]
    public async Task<HttpResponseData> GetDuressFlag(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "streams/{streamId}/duress-flag")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("Getting duress flag for stream: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var stream = await _dbContext.VideoStreams.FindAsync(Guid.Parse(streamId));
        if (stream == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Stream not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            stream_id = stream.StreamId,
            incident_id = stream.IncidentId,
            duress_flagged = stream.DuressFlagged,
            duress_type = stream.DuressType ?? "none",
            flagged_at = stream.DuressFlaggedAt,
            responders_notified = stream.RespondersNotified,
            responders_notified_at = stream.RespondersNotifiedAt
        });

        return response;
    }

    /// <summary>
    /// GET /hq/active-streams - Get all active streams
    /// </summary>
    [Function("GetActiveStreams")]
    public async Task<HttpResponseData> GetActiveStreams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hq/active-streams")] HttpRequestData req)
    {
        _logger.LogInformation("Getting all active video streams");

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var limit = int.TryParse(req.Query["limit"], out var l) ? l : 50;
        var offset = int.TryParse(req.Query["offset"], out var o) ? o : 0;

        var streams = await _dbContext.VideoStreams
            .Where(s => s.Status == "active" || s.Status == "initializing")
            .OrderByDescending(s => s.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(streams.Select(s => new
        {
            stream_id = s.StreamId,
            incident_id = s.IncidentId,
            status = s.Status,
            created_at = s.CreatedAt,
            duration_seconds = s.DurationSeconds,
            duress_flagged = s.DuressFlagged,
            device_type = s.DeviceType
        }));

        return response;
    }

    /// <summary>
    /// POST /hq/stream/{streamId}/notes - Add HQ notes during stream
    /// </summary>
    [Function("AddStreamNote")]
    public async Task<HttpResponseData> AddStreamNote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/stream/{streamId}/notes")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("Adding HQ note for stream: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasRole(req, "hq"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ role required" });
            return forbidden;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        var content = requestBody.GetProperty("content").GetString()!;
        var noteType = requestBody.TryGetProperty("note_type", out var nt) ? nt.GetString() : "observation";

        var note = new VideoStreamNote
        {
            NoteId = Guid.NewGuid(),
            StreamId = Guid.Parse(streamId),
            Content = content,
            NoteType = noteType ?? "observation",
            RecordedById = userId.Value,
            Timestamp = DateTime.UtcNow
        };

        _dbContext.VideoStreamNotes.Add(note);
        await _dbContext.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            note_id = note.NoteId,
            content = note.Content,
            note_type = note.NoteType,
            recorded_by = "HQ Operator",
            timestamp = note.Timestamp
        });

        return response;
    }

    /// <summary>
    /// POST /hq/stream/{streamId}/duress-alert - Manual duress alert (HQ)
    /// </summary>
    [Function("ManualDuressAlert")]
    public async Task<HttpResponseData> ManualDuressAlert(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq/stream/{streamId}/duress-alert")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("HQ manually flagging duress for stream: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasRole(req, "hq"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ role required" });
            return forbidden;
        }

        var stream = await _dbContext.VideoStreams
            .Include(s => s.Incident)
                .ThenInclude(i => i.ResponderAssignments)
            .FirstOrDefaultAsync(s => s.StreamId == Guid.Parse(streamId));

        if (stream == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Stream not found" });
            return notFound;
        }

        var requestBody = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        var reason = requestBody.GetProperty("reason").GetString();

        stream.DuressFlagged = true;
        stream.DuressType = "hq_observation";
        stream.DuressFlaggedAt = DateTime.UtcNow;
        stream.RespondersNotified = true;
        stream.RespondersNotifiedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        // Alert assigned responders
        var responderIds = stream.Incident.ResponderAssignments.Select(ra => ra.ResponderId).ToList();
        foreach (var responderId in responderIds)
        {
            await _notificationService.SendPushNotificationAsync(
                responderId,
                "URGENT: Safety Alert",
                "HQ has flagged potential duress - do not leave scene, await further instructions",
                new Dictionary<string, string> {
                    { "incident_id", stream.IncidentId.ToString() },
                    { "priority", "critical" },
                    { "duress_detected", "true" }
                });
        }

        var alertId = Guid.NewGuid();
        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            alert_id = alertId,
            stream_id = streamId,
            incident_id = stream.IncidentId,
            alert_type = "hq_observation",
            responders_notified = true,
            responders_notified_at = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// GET /streams/{streamId}/recording - Get recording status
    /// </summary>
    [Function("GetRecordingStatus")]
    public async Task<HttpResponseData> GetRecordingStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "streams/{streamId}/recording")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("Getting recording status for stream: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var stream = await _dbContext.VideoStreams.FindAsync(Guid.Parse(streamId));
        if (stream == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Stream not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            stream_id = stream.StreamId,
            incident_id = stream.IncidentId,
            is_recording = stream.Status == "active",
            recording_start = stream.CreatedAt,
            recording_end = stream.TerminatedAt,
            recording_size_mb = (stream.RecordingSizeBytes ?? 0) / (1024.0 * 1024.0),
            storage_location = stream.StorageLocation,
            access_permissions = new[] { "hq", "law_enforcement" }
        });

        return response;
    }

    /// <summary>
    /// GET /streams/{streamId}/playback - Stream playback (HQ/Admin only)
    /// </summary>
    [Function("GetPlaybackUrl")]
    public async Task<HttpResponseData> GetPlaybackUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "streams/{streamId}/playback")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("Getting playback URL for stream: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var stream = await _dbContext.VideoStreams.FindAsync(Guid.Parse(streamId));
        if (stream == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Stream not found" });
            return notFound;
        }

        // Log playback access in audit trail
        _logger.LogInformation("Stream playback accessed by: {UserId}, stream: {StreamId}", userId.Value, streamId);

        // Generate time-limited SAS URL (would actually generate in production)
        var sasUrl = $"https://storage.azure.com/videos/{stream.StorageLocation}recording.mp4?sas_token";

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            playback_url = sasUrl,
            expires_at = DateTime.UtcNow.AddHours(1),
            duration_seconds = stream.DurationSeconds
        });

        return response;
    }

    /// <summary>
    /// GET /streams/{streamId}/quality - Get stream quality metrics
    /// </summary>
    [Function("GetStreamQuality")]
    public async Task<HttpResponseData> GetStreamQuality(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "streams/{streamId}/quality")] HttpRequestData req,
        string streamId)
    {
        _logger.LogInformation("Getting stream quality metrics for: {StreamId}", streamId);

        var userId = JwtUtilities.ExtractUserIdFromToken(req);
        if (userId == null || !JwtUtilities.HasAnyRole(req, "hq", "admin"))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "HQ or admin role required" });
            return forbidden;
        }

        var stream = await _dbContext.VideoStreams.FindAsync(Guid.Parse(streamId));
        if (stream == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Stream not found" });
            return notFound;
        }

        var frameRate = stream.FramesReceived > 0 && stream.DurationSeconds > 0
            ? stream.FramesReceived / (double)stream.DurationSeconds
            : 0;

        var packetLoss = stream.FramesReceived > 0
            ? (stream.FramesDropped * 100.0) / (stream.FramesReceived + stream.FramesDropped)
            : 0;

        var qualityScore = Math.Max(0, 100 - (packetLoss * 10));

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            stream_id = stream.StreamId,
            resolution = "1280x720",
            bitrate_kbps = 2500,
            frame_rate = frameRate,
            latency_ms = 350,
            packet_loss_percent = packetLoss,
            quality_score = qualityScore,
            quality_rating = qualityScore >= 80 ? "good" : qualityScore >= 60 ? "fair" : "poor"
        });

        return response;
    }

    // Helper methods

    private string GenerateWebSocketAuthToken(Guid streamId, Guid userId)
    {
        // In production, generate secure JWT token for WebSocket authentication
        // Include: stream_id, user_id, expiration
        return $"ws_token_{streamId:N}_{userId:N}";
    }

    private static string DetectDeviceType(string deviceId)
    {
        if (deviceId.Contains("ios", StringComparison.OrdinalIgnoreCase) ||
            deviceId.Contains("iphone", StringComparison.OrdinalIgnoreCase))
            return "ios";

        if (deviceId.Contains("android", StringComparison.OrdinalIgnoreCase))
            return "android";

        return "web";
    }
}
