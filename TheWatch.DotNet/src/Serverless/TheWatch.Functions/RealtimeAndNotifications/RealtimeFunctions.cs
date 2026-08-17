using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for real-time sync via SignalR Service.
/// Implements endpoints from realtime-sync-api.yaml
/// </summary>
/// <remarks>
/// Key Features:
/// - SignalR connection negotiation (Serverless mode)
/// - Incident update subscriptions
/// - HQ broadcast channel subscriptions
/// - Evacuation tracking subscriptions
/// - Real-time location streaming during incidents
///
/// Architecture:
/// - Azure SignalR Service in Serverless mode
/// - Clients negotiate connection via HTTPS, then upgrade to WebSocket
/// - JWT authentication for all connections
/// </remarks>
public class RealtimeFunctions
{
    private readonly ILogger<RealtimeFunctions> _logger;
    private readonly IRealtimeService _realtimeService;

    public RealtimeFunctions(
        ILogger<RealtimeFunctions> logger,
        IRealtimeService realtimeService)
    {
        _logger = logger;
        _realtimeService = realtimeService;
    }

    /// <summary>
    /// POST /negotiate - Negotiate SignalR connection
    /// </summary>
    /// <remarks>
    /// Returns SignalR connection info for establishing WebSocket connection.
    /// Flow:
    /// 1. Client POSTs to /negotiate with bearer token
    /// 2. Server returns SignalR endpoint + access token
    /// 3. Client connects to SignalR WebSocket using returned credentials
    /// </remarks>
    [Function("NegotiateSignalR")]
    public async Task<HttpResponseData> NegotiateSignalR(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "negotiate")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "watchHub", ConnectionStringSetting = "AzureSignalRConnectionString")] SignalRConnectionInfo connectionInfo)
    {
        _logger.LogInformation("Negotiating SignalR connection");

        try
        {
            // 1. Validate JWT bearer token
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            // 2-5. Return connection info (SignalR binding handles access token generation)
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                url = connectionInfo.Url,
                accessToken = connectionInfo.AccessToken
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error negotiating SignalR connection");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /subscriptions/incidents/{incidentId} - Subscribe to incident updates
    /// </summary>
    /// <remarks>
    /// Authorized Users:
    /// - HQ/Admin (all incidents)
    /// - Assigned responders (specific incident)
    /// - Summoner (their own incident)
    ///
    /// Events Received:
    /// - responder_accepted, responder_on_scene, responder_distress
    /// - hq_broadcast, incident_resolved, disagreement_flagged
    /// </remarks>
    [Function("SubscribeToIncident")]
    public async Task<SubscribeResult> SubscribeToIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscriptions/incidents/{incidentId}")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Subscribing to incident updates: {IncidentId}", incidentId);

        try
        {
            // 1. Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return new SubscribeResult { HttpResponse = unauthorized };
            }

            // Parse incident ID
            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID format" });
                return new SubscribeResult { HttpResponse = badRequest };
            }

            // 2-6. Subscribe via service (handles authorization, connection management, and storage)
            var connectionId = req.Headers.TryGetValues("X-SignalR-Connection-Id", out var connIds)
                ? connIds.FirstOrDefault() ?? ""
                : "";

            var subscription = await _realtimeService.SubscribeToIncidentAsync(userId.Value, incidentGuid, connectionId);

            // Create SignalR group action to add user to incident group
            var groupAction = new SignalRGroupAction(SignalRGroupActionType.Add)
            {
                UserId = userId.Value.ToString(),
                GroupName = $"incident-{incidentId}",
                ConnectionId = connectionId
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                subscriptionId = subscription.SubscriptionId.ToString(),
                channel = $"incident-{incidentId}",
                subscribedAt = subscription.CreatedAt
            });

            return new SubscribeResult
            {
                HttpResponse = response,
                SignalRGroupActions = new[] { groupAction }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to incident {IncidentId}", incidentId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return new SubscribeResult { HttpResponse = errorResponse };
        }
    }

    /// <summary>
    /// DELETE /subscriptions/incidents/{incidentId} - Unsubscribe from incident updates
    /// </summary>
    [Function("UnsubscribeFromIncident")]
    public async Task<SubscribeResult> UnsubscribeFromIncident(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "subscriptions/incidents/{incidentId}")] HttpRequestData req,
        string incidentId)
    {
        _logger.LogInformation("Unsubscribing from incident updates: {IncidentId}", incidentId);

        try
        {
            // 1. Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return new SubscribeResult { HttpResponse = unauthorized };
            }

            // Parse incident ID
            if (!Guid.TryParse(incidentId, out var incidentGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid incident ID format" });
                return new SubscribeResult { HttpResponse = badRequest };
            }

            // 2-4. Unsubscribe via service
            var connectionId = req.Headers.TryGetValues("X-SignalR-Connection-Id", out var connIds)
                ? connIds.FirstOrDefault() ?? ""
                : "";

            await _realtimeService.UnsubscribeFromIncidentAsync(userId.Value, incidentGuid);

            var groupAction = new SignalRGroupAction(SignalRGroupActionType.Remove)
            {
                UserId = userId.Value.ToString(),
                GroupName = $"incident-{incidentId}",
                ConnectionId = connectionId
            };

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return new SubscribeResult
            {
                HttpResponse = response,
                SignalRGroupActions = new[] { groupAction }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unsubscribing from incident {IncidentId}", incidentId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return new SubscribeResult { HttpResponse = errorResponse };
        }
    }

    /// <summary>
    /// POST /subscriptions/hq-broadcasts - Subscribe to HQ broadcasts
    /// </summary>
    /// <remarks>
    /// Responders Only.
    /// Used for "Voice of God" commands like "WEAPON SPOTTED - RETREAT".
    /// Critical safety channel for all active responders.
    /// </remarks>
    [Function("SubscribeToHqBroadcasts")]
    public async Task<SubscribeResult> SubscribeToHqBroadcasts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscriptions/hq-broadcasts")] HttpRequestData req)
    {
        _logger.LogInformation("Subscribing to HQ broadcasts");

        try
        {
            // 1. Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return new SubscribeResult { HttpResponse = unauthorized };
            }

            // 2. Verify user has 'responder' role
            if (!JwtUtilities.HasRole(req, "responder"))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Only responders can subscribe to HQ broadcasts" });
                return new SubscribeResult { HttpResponse = forbidden };
            }

            // 3-4. Subscribe via service
            var connectionId = req.Headers.TryGetValues("X-SignalR-Connection-Id", out var connIds)
                ? connIds.FirstOrDefault() ?? ""
                : "";

            var subscription = await _realtimeService.SubscribeToHqBroadcastsAsync(userId.Value, connectionId);

            var groupAction = new SignalRGroupAction(SignalRGroupActionType.Add)
            {
                UserId = userId.Value.ToString(),
                GroupName = "hq-broadcasts",
                ConnectionId = connectionId
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                subscriptionId = subscription.SubscriptionId.ToString(),
                channel = "hq-broadcasts",
                subscribedAt = subscription.CreatedAt
            });

            return new SubscribeResult
            {
                HttpResponse = response,
                SignalRGroupActions = new[] { groupAction }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to HQ broadcasts");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return new SubscribeResult { HttpResponse = errorResponse };
        }
    }

    /// <summary>
    /// DELETE /subscriptions/hq-broadcasts - Unsubscribe from HQ broadcasts
    /// </summary>
    [Function("UnsubscribeFromHqBroadcasts")]
    public async Task<SubscribeResult> UnsubscribeFromHqBroadcasts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "subscriptions/hq-broadcasts")] HttpRequestData req)
    {
        _logger.LogInformation("Unsubscribing from HQ broadcasts");

        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return new SubscribeResult { HttpResponse = unauthorized };
            }

            await _realtimeService.UnsubscribeFromHqBroadcastsAsync(userId.Value);

            var connectionId = req.Headers.TryGetValues("X-SignalR-Connection-Id", out var connIds)
                ? connIds.FirstOrDefault() ?? ""
                : "";

            var groupAction = new SignalRGroupAction(SignalRGroupActionType.Remove)
            {
                UserId = userId.Value.ToString(),
                GroupName = "hq-broadcasts",
                ConnectionId = connectionId
            };

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return new SubscribeResult
            {
                HttpResponse = response,
                SignalRGroupActions = new[] { groupAction }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unsubscribing from HQ broadcasts");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return new SubscribeResult { HttpResponse = errorResponse };
        }
    }

    /// <summary>
    /// POST /subscriptions/evacuations/{evacuationId} - Subscribe to evacuation updates
    /// </summary>
    /// <remarks>
    /// Real-time updates for evacuation matching, route changes, status updates.
    /// </remarks>
    [Function("SubscribeToEvacuation")]
    public async Task<SubscribeResult> SubscribeToEvacuation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscriptions/evacuations/{evacuationId}")] HttpRequestData req,
        string evacuationId)
    {
        _logger.LogInformation("Subscribing to evacuation updates: {EvacuationId}", evacuationId);

        try
        {
            // 1. Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return new SubscribeResult { HttpResponse = unauthorized };
            }

            // Parse evacuation ID
            if (!Guid.TryParse(evacuationId, out var evacuationGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid evacuation ID format" });
                return new SubscribeResult { HttpResponse = badRequest };
            }

            // 2-4. Subscribe via service (handles authorization check)
            var connectionId = req.Headers.TryGetValues("X-SignalR-Connection-Id", out var connIds)
                ? connIds.FirstOrDefault() ?? ""
                : "";

            var subscription = await _realtimeService.SubscribeToEvacuationAsync(userId.Value, evacuationGuid, connectionId);

            var groupAction = new SignalRGroupAction(SignalRGroupActionType.Add)
            {
                UserId = userId.Value.ToString(),
                GroupName = $"evacuation-{evacuationId}",
                ConnectionId = connectionId
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                subscriptionId = subscription.SubscriptionId.ToString(),
                channel = $"evacuation-{evacuationId}",
                subscribedAt = subscription.CreatedAt
            });

            return new SubscribeResult
            {
                HttpResponse = response,
                SignalRGroupActions = new[] { groupAction }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to evacuation {EvacuationId}", evacuationId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return new SubscribeResult { HttpResponse = errorResponse };
        }
    }

    /// <summary>
    /// POST /streaming/location/{sessionId} - Start location streaming session
    /// </summary>
    /// <remarks>
    /// Initiates ephemeral location streaming during active incident or evacuation.
    /// Location points are TTL'd and deleted after session completes.
    /// Rate Limit: 1 update per 5 seconds (enforced server-side).
    /// </remarks>
    [Function("StartLocationStreaming")]
    public async Task<HttpResponseData> StartLocationStreaming(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "streaming/location/{sessionId}")] HttpRequestData req,
        string sessionId)
    {
        _logger.LogInformation("Starting location streaming session: {SessionId}", sessionId);

        try
        {
            // 1. Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            // Parse session ID
            if (!Guid.TryParse(sessionId, out var sessionGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid session ID format" });
                return badRequest;
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var streamingRequest = JsonSerializer.Deserialize<LocationStreamingRequest>(requestBody);

            // 2-7. Start streaming via service (handles validation, authorization, and session creation)
            var updateInterval = Math.Max(streamingRequest?.UpdateIntervalSeconds ?? 10, 5);
            var sessionType = streamingRequest?.SessionType ?? "incident";

            var session = await _realtimeService.StartLocationStreamingAsync(
                userId.Value,
                sessionGuid,
                sessionType,
                updateInterval
            );

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                sessionId = session.SessionId.ToString(),
                status = session.Status,
                updateIntervalSeconds = session.UpdateIntervalSeconds,
                ttlSeconds = session.TtlSeconds
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting location streaming session {SessionId}", sessionId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// DELETE /streaming/location/{sessionId} - Stop location streaming
    /// </summary>
    [Function("StopLocationStreaming")]
    public async Task<HttpResponseData> StopLocationStreaming(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "streaming/location/{sessionId}")] HttpRequestData req,
        string sessionId)
    {
        _logger.LogInformation("Stopping location streaming session: {SessionId}", sessionId);

        try
        {
            // 1. Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            // Parse session ID
            if (!Guid.TryParse(sessionId, out var sessionGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid session ID format" });
                return badRequest;
            }

            // 2-4. Stop streaming via service (handles ownership verification and cleanup)
            await _realtimeService.StopLocationStreamingAsync(userId.Value, sessionGuid);

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping location streaming session {SessionId}", sessionId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /streaming/location/{sessionId}/update - Push location update
    /// </summary>
    /// <remarks>
    /// Sends single location update to active streaming session.
    /// Server broadcasts to all subscribers via SignalR.
    /// Rate limit: 1 update per 5 seconds.
    /// </remarks>
    [Function("PushLocationUpdate")]
    public async Task<LocationUpdateResult> PushLocationUpdate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "streaming/location/{sessionId}/update")] HttpRequestData req,
        string sessionId)
    {
        _logger.LogInformation("Pushing location update for session: {SessionId}", sessionId);

        try
        {
            // 1. Validate authentication
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (userId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return new LocationUpdateResult { HttpResponse = unauthorized };
            }

            // Parse session ID
            if (!Guid.TryParse(sessionId, out var sessionGuid))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid session ID format" });
                return new LocationUpdateResult { HttpResponse = badRequest };
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var locationUpdate = JsonSerializer.Deserialize<LocationUpdateDto>(requestBody);

            if (locationUpdate == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid location update" });
                return new LocationUpdateResult { HttpResponse = badRequest };
            }

            // 2-5. Broadcast location update via service (handles validation, rate limiting, and storage)
            var locationRecord = new Core.Entities.LocationRecord
            {
                LocationId = Guid.NewGuid(),
                UserId = userId.Value,
                Latitude = locationUpdate.Latitude,
                Longitude = locationUpdate.Longitude,
                Altitude = locationUpdate.Altitude,
                Accuracy = locationUpdate.Accuracy,
                Timestamp = locationUpdate.Timestamp,
                Geohash = locationUpdate.Geohash ?? "",
                ServerTimestamp = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            await _realtimeService.BroadcastLocationUpdateAsync(sessionGuid, locationRecord);

            // Broadcast to session subscribers via SignalR
            var signalRMessage = new SignalRMessageAction("locationUpdate")
            {
                GroupName = $"location-session-{sessionId}",
                Arguments = new object[] { locationUpdate }
            };

            // 6. Return 202 Accepted
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            return new LocationUpdateResult
            {
                HttpResponse = response,
                SignalRMessages = new[] { signalRMessage }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing location update for session {SessionId}", sessionId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return new LocationUpdateResult { HttpResponse = errorResponse };
        }
    }

    /// <summary>
    /// Broadcast incident event to subscribers
    /// </summary>
    /// <remarks>
    /// Called by other functions when incident events occur.
    /// Broadcasts to all users subscribed to the incident channel.
    /// </remarks>
    [Function("BroadcastIncidentEvent")]
    [SignalROutput(HubName = "watchHub", ConnectionStringSetting = "AzureSignalRConnectionString")]
    public async Task<SignalRMessageAction?> BroadcastIncidentEvent(
        [ServiceBusTrigger("incident-events-queue", Connection = "ServiceBusConnection")] string message,
        FunctionContext context)
    {
        var logger = context.GetLogger("BroadcastIncidentEvent");
        logger.LogInformation("Broadcasting incident event");

        try
        {
            var incidentEvent = JsonSerializer.Deserialize<IncidentEventDto>(message);

            if (incidentEvent != null)
            {
                logger.LogInformation("Incident event broadcast to group: incident-{IncidentId}", incidentEvent.IncidentId);
                
                return new SignalRMessageAction("incidentUpdate")
                {
                    GroupName = $"incident-{incidentEvent.IncidentId}",
                    Arguments = new object[] { incidentEvent }
                };
            }

            await Task.CompletedTask;
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error broadcasting incident event");
            throw;
        }
    }
}

/// <summary>
/// Result type for subscription functions with multiple outputs
/// </summary>
public class SubscribeResult
{
    [HttpResult]
    public HttpResponseData? HttpResponse { get; set; }
    
    [SignalROutput(HubName = "watchHub", ConnectionStringSetting = "AzureSignalRConnectionString")]
    public SignalRGroupAction[]? SignalRGroupActions { get; set; }
}

/// <summary>
/// Result type for location update function with multiple outputs
/// </summary>
public class LocationUpdateResult
{
    [HttpResult]
    public HttpResponseData? HttpResponse { get; set; }
    
    [SignalROutput(HubName = "watchHub", ConnectionStringSetting = "AzureSignalRConnectionString")]
    public SignalRMessageAction[]? SignalRMessages { get; set; }
}

/// <summary>
/// Request model for location streaming
/// </summary>
public class LocationStreamingRequest
{
    public int UpdateIntervalSeconds { get; set; } = 10;
    public string? SessionType { get; set; }
}

/// <summary>
/// DTO for location updates
/// </summary>
public class LocationUpdateDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
    public double? Accuracy { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Geohash { get; set; }
}

/// <summary>
/// DTO for incident events
/// </summary>
public class IncidentEventDto
{
    public string IncidentId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// SignalR group action type enum
/// </summary>
public enum SignalRGroupActionType
{
    Add,
    Remove
}

/// <summary>
/// SignalR group action model for Worker model
/// </summary>
public class SignalRGroupAction
{
    public SignalRGroupAction(SignalRGroupActionType actionType)
    {
        Action = actionType;
    }
    
    public string? UserId { get; set; }
    public string? ConnectionId { get; set; }
    public required string GroupName { get; set; }
    public SignalRGroupActionType Action { get; set; }
}

/// <summary>
/// SignalR message action model for Worker model
/// </summary>
public class SignalRMessageAction
{
    public SignalRMessageAction(string target)
    {
        Target = target;
    }

    public string Target { get; set; }
    public string? GroupName { get; set; }
    public string? UserId { get; set; }
    public object[]? Arguments { get; set; }
}

/// <summary>
/// SignalR connection info returned from negotiation
/// </summary>
public class SignalRConnectionInfo
{
    public string Url { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}
