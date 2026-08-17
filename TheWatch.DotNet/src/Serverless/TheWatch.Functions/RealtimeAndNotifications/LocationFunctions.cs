using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for location tracking and geospatial queries.
/// Implements endpoints from location-api.yaml
/// </summary>
/// <remarks>
/// Key Features:
/// - High-frequency location updates (500 req/min rate limit)
/// - Geohash-based proximity searches
/// - Batch location updates for efficiency
/// - Ephemeral data with 1-hour TTL
/// </remarks>
public class LocationFunctions
{
    private readonly ILogger<LocationFunctions> _logger;
    private readonly ILocationService _locationService;
    private readonly GeohashService _geohashService;

    public LocationFunctions(
        ILogger<LocationFunctions> logger,
        ILocationService locationService,
        GeohashService geohashService)
    {
        _logger = logger;
        _locationService = locationService;
        _geohashService = geohashService;
    }

    /// <summary>
    /// PUT /users/{userId}/location - Update user location
    /// </summary>
    /// <remarks>
    /// High-frequency endpoint (500 req/min limit per user).
    /// Automatically calculates geohash for efficient spatial queries.
    /// If user is in an active incident, location is streamed to incident timeline.
    /// Location data is ephemeral with 1-hour TTL.
    /// </remarks>
    [Function("UpdateUserLocation")]
    public async Task<HttpResponseData> UpdateUserLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{userId}/location")] HttpRequestData req,
        string userId)
    {
        _logger.LogInformation("Updating location for user: {UserId}", userId);

        try
        {
            // 1. Validate authentication (JWT bearer token)
            var authenticatedUserId = JwtUtilities.ExtractUserIdFromToken(req);
            if (authenticatedUserId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            // 2. Verify userId matches authenticated user (unless HQ/admin)
            if (!Guid.TryParse(userId, out var targetUserId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid user ID format" });
                return badRequest;
            }

            if (authenticatedUserId.Value != targetUserId && !JwtUtilities.HasAnyRole(req, "hq", "admin"))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Cannot update location for another user" });
                return forbidden;
            }

            // 3. Parse LocationUpdate from request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var locationUpdate = JsonSerializer.Deserialize<LocationUpdateRequest>(requestBody);

            if (locationUpdate == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid location update" });
                return badRequest;
            }

            // 4-8. Update location via service (handles geohash calculation, storage, and SignalR)
            var locationRecord = await _locationService.UpdateLocationAsync(
                targetUserId,
                locationUpdate.Latitude,
                locationUpdate.Longitude,
                locationUpdate.Altitude,
                locationUpdate.Accuracy,
                locationUpdate.Heading,
                locationUpdate.Speed,
                locationUpdate.Timestamp
            );

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                userId = locationRecord.UserId.ToString(),
                latitude = locationRecord.Latitude,
                longitude = locationRecord.Longitude,
                altitude = locationRecord.Altitude,
                accuracy = locationRecord.Accuracy,
                heading = locationRecord.Heading,
                speed = locationRecord.Speed,
                timestamp = locationRecord.Timestamp,
                geohash = locationRecord.Geohash,
                serverTimestamp = locationRecord.ServerTimestamp
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating location for user {UserId}", userId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /users/{userId}/location - Get user's last known location
    /// </summary>
    [Function("GetUserLocation")]
    public async Task<HttpResponseData> GetUserLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/location")] HttpRequestData req,
        string userId)
    {
        _logger.LogInformation("Getting location for user: {UserId}", userId);

        try
        {
            // 1. Validate authentication
            var authenticatedUserId = JwtUtilities.ExtractUserIdFromToken(req);
            if (authenticatedUserId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            // Parse target user ID
            if (!Guid.TryParse(userId, out var targetUserId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid user ID format" });
                return badRequest;
            }

            // 2. Check authorization (user can see own location, HQ/assigned responders can see others)
            if (authenticatedUserId.Value != targetUserId && !JwtUtilities.HasAnyRole(req, "hq", "admin", "responder"))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Cannot view location for another user" });
                return forbidden;
            }

            // 3. Retrieve from cache/service
            var locationRecord = await _locationService.GetUserLocationAsync(targetUserId);

            // 4. Return 404 if not found or expired
            if (locationRecord == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Location not found or expired" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                userId = locationRecord.UserId.ToString(),
                latitude = locationRecord.Latitude,
                longitude = locationRecord.Longitude,
                altitude = locationRecord.Altitude,
                accuracy = locationRecord.Accuracy,
                heading = locationRecord.Heading,
                speed = locationRecord.Speed,
                timestamp = locationRecord.Timestamp,
                geohash = locationRecord.Geohash,
                serverTimestamp = locationRecord.ServerTimestamp
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location for user {UserId}", userId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /users/{userId}/location/history - Get location breadcrumbs
    /// </summary>
    /// <remarks>
    /// Retrieves historical location points for post-incident analysis or evidence.
    /// Respects 1-hour TTL on location data unless associated with an incident.
    /// </remarks>
    [Function("GetLocationHistory")]
    public async Task<HttpResponseData> GetLocationHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/location/history")] HttpRequestData req,
        string userId)
    {
        _logger.LogInformation("Getting location history for user: {UserId}", userId);

        try
        {
            // 1. Validate authentication
            var authenticatedUserId = JwtUtilities.ExtractUserIdFromToken(req);
            if (authenticatedUserId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            // Parse target user ID
            if (!Guid.TryParse(userId, out var targetUserId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid user ID format" });
                return badRequest;
            }

            // 2. Check authorization (HQ/admin or user's own history)
            if (authenticatedUserId.Value != targetUserId && !JwtUtilities.HasAnyRole(req, "hq", "admin"))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { code = "FORBIDDEN", message = "Cannot view location history for another user" });
                return forbidden;
            }

            // 3. Parse startTime and endTime from query params
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            DateTime? startTime = null;
            DateTime? endTime = null;

            if (DateTime.TryParse(query["startTime"], out var parsedStart))
            {
                startTime = parsedStart;
            }

            if (DateTime.TryParse(query["endTime"], out var parsedEnd))
            {
                endTime = parsedEnd;
            }

            // 4. Query location history from database
            var locationHistory = await _locationService.GetLocationHistoryAsync(targetUserId, startTime, endTime);

            // 5. Return array of LocationRecord objects
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(locationHistory.Select(loc => new
            {
                userId = loc.UserId.ToString(),
                latitude = loc.Latitude,
                longitude = loc.Longitude,
                altitude = loc.Altitude,
                accuracy = loc.Accuracy,
                heading = loc.Heading,
                speed = loc.Speed,
                timestamp = loc.Timestamp,
                geohash = loc.Geohash,
                serverTimestamp = loc.ServerTimestamp,
                incidentId = loc.IncidentId?.ToString(),
                evacuationId = loc.EvacuationId?.ToString()
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location history for user {UserId}", userId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /spatial/nearby-users - Find users within radius or geohash area
    /// </summary>
    /// <remarks>
    /// Used by Dispatch system to find eligible responders.
    /// Uses geohash prefix matching for efficient spatial queries.
    /// Filters by user type (responder, community_member, all).
    /// </remarks>
    [Function("FindNearbyUsers")]
    public async Task<HttpResponseData> FindNearbyUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "spatial/nearby-users")] HttpRequestData req)
    {
        _logger.LogInformation("Finding nearby users");

        try
        {
            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var latitudeStr = query["latitude"];
            var longitudeStr = query["longitude"];
            var radiusMetersStr = query["radiusMeters"] ?? "500";
            var userType = query["userType"] ?? "responder";
            var excludeUserId = query["excludeUserId"];

            if (string.IsNullOrEmpty(latitudeStr) || string.IsNullOrEmpty(longitudeStr))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Latitude and longitude are required" });
                return badRequest;
            }

            var latitude = double.Parse(latitudeStr);
            var longitude = double.Parse(longitudeStr);
            var radiusMeters = int.Parse(radiusMetersStr);

            // Validate authentication for HQ/responder queries
            var authenticatedUserId = JwtUtilities.ExtractUserIdFromToken(req);
            if (authenticatedUserId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            // Parse optional excludeUserId
            Guid? excludeUserIdGuid = null;
            if (!string.IsNullOrEmpty(excludeUserId) && Guid.TryParse(excludeUserId, out var parsedExclude))
            {
                excludeUserIdGuid = parsedExclude;
            }

            // 1-8. Find nearby users via service (handles geohash calculation and proximity filtering)
            var nearbyUsers = await _locationService.FindNearbyUsersAsync(
                latitude,
                longitude,
                radiusMeters,
                userType,
                excludeUserIdGuid
            );

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(nearbyUsers.Select(u => new
            {
                userId = u.UserId.ToString(),
                userType = u.UserType,
                location = new
                {
                    latitude = u.Latitude,
                    longitude = u.Longitude,
                    geohash = _geohashService.Encode(u.Latitude, u.Longitude, 9),
                    lastSeen = u.LastSeen
                },
                distanceMeters = u.DistanceMeters
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding nearby users");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /spatial/geohash - Calculate geohash for coordinates
    /// </summary>
    /// <remarks>
    /// Utility endpoint for client-side geohash calculation.
    /// Default precision is 9 characters (~5m accuracy).
    /// </remarks>
    [Function("GetGeohash")]
    public async Task<HttpResponseData> GetGeohash(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "spatial/geohash")] HttpRequestData req)
    {
        _logger.LogInformation("Calculating geohash");

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var latitudeStr = query["latitude"];
            var longitudeStr = query["longitude"];
            var precisionStr = query["precision"] ?? "9";

            if (string.IsNullOrEmpty(latitudeStr) || string.IsNullOrEmpty(longitudeStr))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Latitude and longitude are required" });
                return badRequest;
            }

            var latitude = double.Parse(latitudeStr);
            var longitude = double.Parse(longitudeStr);
            var precision = int.Parse(precisionStr);

            if (precision < 1 || precision > 12)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Precision must be between 1 and 12" });
                return badRequest;
            }

            var geohash = CalculateGeohash(latitude, longitude, precision);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                geohash = geohash,
                latitude = latitude,
                longitude = longitude
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating geohash");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /location/batch - Batch location updates
    /// </summary>
    /// <remarks>
    /// Accepts multiple location updates for efficiency.
    /// Useful for offline sync scenarios.
    /// All updates must include idempotency keys.
    /// </remarks>
    [Function("BatchUpdateLocations")]
    public async Task<HttpResponseData> BatchUpdateLocations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "location/batch")] HttpRequestData req)
    {
        _logger.LogInformation("Processing batch location updates");

        try
        {
            // 1. Validate authentication
            var authenticatedUserId = JwtUtilities.ExtractUserIdFromToken(req);
            if (authenticatedUserId == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { code = "UNAUTHORIZED", message = "Invalid or missing authentication token" });
                return unauthorized;
            }

            // 2. Parse array of LocationUpdate objects
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var updates = JsonSerializer.Deserialize<LocationUpdateRequest[]>(requestBody);

            if (updates == null || updates.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid or empty batch update request" });
                return badRequest;
            }

            // 3-7. Process via service (handles validation, chronological order, geohash, and storage)
            var locationUpdates = updates.Select(u => new Core.Interfaces.LocationUpdate
            {
                UserId = authenticatedUserId.Value,
                Latitude = u.Latitude,
                Longitude = u.Longitude,
                Altitude = u.Altitude,
                Accuracy = u.Accuracy,
                Heading = u.Heading,
                Speed = u.Speed,
                Timestamp = u.Timestamp,
                IdempotencyKey = u.IdempotencyKey
            }).ToList();

            var result = await _locationService.BatchUpdateLocationsAsync(locationUpdates);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                processed = result.SuccessfulUpdates,
                failed = result.FailedUpdates,
                errors = result.Errors,
                message = "Batch location updates processed successfully"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch location updates");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Calculate geohash for given coordinates
    /// </summary>
    private string CalculateGeohash(double latitude, double longitude, int precision)
    {
        return _geohashService.Encode(latitude, longitude, precision);
    }
}

/// <summary>
/// Request model for location updates
/// </summary>
public class LocationUpdateRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
    public double? Accuracy { get; set; }
    public double? Heading { get; set; }
    public double? Speed { get; set; }
    public DateTime? Timestamp { get; set; }
    public string? IdempotencyKey { get; set; }
}
