using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for designated responder scheduling operations.
/// Handles schedule creation, updates, conflict detection, and availability calculation.
/// </summary>
public class ResponderScheduleFunctions
{
    private readonly IResponderScheduleService _scheduleService;
    private readonly IResponderScheduleRepository _scheduleRepository;
    private readonly ILogger<ResponderScheduleFunctions> _logger;

    public ResponderScheduleFunctions(
        IResponderScheduleService scheduleService,
        IResponderScheduleRepository scheduleRepository,
        ILogger<ResponderScheduleFunctions> logger)
    {
        _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
        _scheduleRepository = scheduleRepository ?? throw new ArgumentNullException(nameof(scheduleRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ============================================
    // Schedule CRUD Operations
    // ============================================

    /// <summary>
    /// POST /responders/{responderId}/schedules
    /// Creates a new responder schedule with validation and conflict detection.
    /// </summary>
    [Function("CreateResponderSchedule")]
    public async Task<HttpResponseData> CreateSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/{responderId}/schedules")]
        HttpRequestData req,
        string responderId)
    {
        try
        {
            // Parse JWT and validate user has permission to create schedules
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);
            var userId = JwtUtilities.GetUserIdFromClaims(claims);

            if (!Guid.TryParse(responderId, out var responderGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid responder ID");
            }

            // Ensure user is creating schedule for themselves or is HQ/admin
            if (userId != responderGuid && !JwtUtilities.HasRole(claims, "hq") && !JwtUtilities.HasRole(claims, "admin"))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.Forbidden, "Cannot create schedule for another user");
            }

            // Parse request body
            var body = await req.ReadAsStringAsync() ?? string.Empty;
            var createRequest = JsonSerializer.Deserialize<CreateScheduleRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (createRequest == null)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid request body");
            }

            // Create schedule entity
            var schedule = new DesignatedResponderSchedule
            {
                ResponderId = responderGuid,
                CommitmentType = createRequest.CommitmentType ?? "recurring",
                LocationLat = createRequest.LocationLat,
                LocationLng = createRequest.LocationLng,
                LocationGeohash = createRequest.LocationGeohash ?? string.Empty,
                LocationName = createRequest.LocationName,
                RadiusMeters = createRequest.RadiusMeters,
                StartTime = createRequest.StartTime,
                EndTime = createRequest.EndTime,
                Pattern = createRequest.Pattern,
                DaysOfWeek = createRequest.DaysOfWeek,
                DayOfMonth = createRequest.DayOfMonth,
                RecurrenceInterval = createRequest.RecurrenceInterval ?? 1,
                DailyStartTime = createRequest.DailyStartTime,
                DailyEndTime = createRequest.DailyEndTime,
                TimeZone = createRequest.TimeZone ?? "UTC",
                EffectiveStartDate = createRequest.EffectiveStartDate ?? DateTime.UtcNow.Date,
                EffectiveEndDate = createRequest.EffectiveEndDate,
                CreatedBy = userId ?? Guid.Empty
            };

            // Create schedule (includes validation and conflict detection)
            var created = await _scheduleService.CreateScheduleAsync(schedule);

            // Generate preview for next 30 days
            var preview = await _scheduleService.GenerateSchedulePreviewAsync(
                created,
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(30));

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                schedule = created,
                preview = preview.Take(10) // First 10 occurrences
            });

            return response;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to create schedule for responder {ResponderId}", responderId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating schedule for responder {ResponderId}", responderId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to create schedule");
        }
    }

    /// <summary>
    /// GET /responders/{responderId}/schedules
    /// Gets all schedules for a responder.
    /// </summary>
    [Function("GetResponderSchedules")]
    public async Task<HttpResponseData> GetSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "responders/{responderId}/schedules")]
        HttpRequestData req,
        string responderId)
    {
        try
        {
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);
            var userId = JwtUtilities.GetUserIdFromClaims(claims);

            if (!Guid.TryParse(responderId, out var responderGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid responder ID");
            }

            // Ensure user can view schedules (self, HQ, or admin)
            if (userId != responderGuid && !JwtUtilities.HasRole(claims, "hq") && !JwtUtilities.HasRole(claims, "admin"))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.Forbidden, "Cannot view schedules for another user");
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var includeInactive = bool.Parse(query["includeInactive"] ?? "false");

            var schedules = await _scheduleService.GetSchedulesByResponderIdAsync(responderGuid, includeInactive);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                schedules = schedules,
                count = schedules.Count
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schedules for responder {ResponderId}", responderId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to get schedules");
        }
    }

    /// <summary>
    /// GET /responders/{responderId}/schedules/{scheduleId}
    /// Gets a specific schedule with details.
    /// </summary>
    [Function("GetResponderScheduleById")]
    public async Task<HttpResponseData> GetScheduleById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "responders/{responderId}/schedules/{scheduleId}")]
        HttpRequestData req,
        string responderId,
        string scheduleId)
    {
        try
        {
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);
            var userId = JwtUtilities.GetUserIdFromClaims(claims);

            if (!Guid.TryParse(responderId, out var responderGuid) || !Guid.TryParse(scheduleId, out var scheduleGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid ID");
            }

            var schedule = await _scheduleService.GetScheduleByIdAsync(scheduleGuid);
            if (schedule == null)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.NotFound, "Schedule not found");
            }

            // Verify responder ID matches
            if (schedule.ResponderId != responderGuid)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Schedule does not belong to this responder");
            }

            // Ensure user can view schedule
            if (userId != responderGuid && !JwtUtilities.HasRole(claims, "hq") && !JwtUtilities.HasRole(claims, "admin"))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.Forbidden, "Cannot view schedule");
            }

            // Calculate next occurrences
            var nextOccurrences = await _scheduleService.GenerateSchedulePreviewAsync(
                schedule,
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(30));

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                schedule = schedule,
                nextOccurrences = nextOccurrences.Take(10)
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting schedule {ScheduleId}", scheduleId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to get schedule");
        }
    }

    /// <summary>
    /// PUT /responders/{responderId}/schedules/{scheduleId}
    /// Updates an existing schedule with validation and conflict detection.
    /// </summary>
    [Function("UpdateResponderSchedule")]
    public async Task<HttpResponseData> UpdateSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "responders/{responderId}/schedules/{scheduleId}")]
        HttpRequestData req,
        string responderId,
        string scheduleId)
    {
        try
        {
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);
            var userId = JwtUtilities.GetUserIdFromClaims(claims);

            if (!Guid.TryParse(responderId, out var responderGuid) || !Guid.TryParse(scheduleId, out var scheduleGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid ID");
            }

            // Ensure user can update schedule
            if (userId != responderGuid && !JwtUtilities.HasRole(claims, "hq") && !JwtUtilities.HasRole(claims, "admin"))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.Forbidden, "Cannot update schedule");
            }

            // Get existing schedule
            var existing = await _scheduleService.GetScheduleByIdAsync(scheduleGuid);
            if (existing == null)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.NotFound, "Schedule not found");
            }

            if (existing.ResponderId != responderGuid)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Schedule does not belong to this responder");
            }

            // Parse update request
            var body = await req.ReadAsStringAsync() ?? string.Empty;
            var updateRequest = JsonSerializer.Deserialize<UpdateScheduleRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateRequest == null)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid request body");
            }

            // Update fields
            if (updateRequest.CommitmentType != null) existing.CommitmentType = updateRequest.CommitmentType;
            if (updateRequest.LocationLat.HasValue) existing.LocationLat = updateRequest.LocationLat.Value;
            if (updateRequest.LocationLng.HasValue) existing.LocationLng = updateRequest.LocationLng.Value;
            if (updateRequest.LocationGeohash != null) existing.LocationGeohash = updateRequest.LocationGeohash;
            if (updateRequest.LocationName != null) existing.LocationName = updateRequest.LocationName;
            if (updateRequest.RadiusMeters.HasValue) existing.RadiusMeters = updateRequest.RadiusMeters.Value;
            if (updateRequest.StartTime.HasValue) existing.StartTime = updateRequest.StartTime;
            if (updateRequest.EndTime.HasValue) existing.EndTime = updateRequest.EndTime;
            if (updateRequest.Pattern.HasValue) existing.Pattern = updateRequest.Pattern.Value;
            if (updateRequest.DaysOfWeek.HasValue) existing.DaysOfWeek = updateRequest.DaysOfWeek;
            if (updateRequest.DayOfMonth.HasValue) existing.DayOfMonth = updateRequest.DayOfMonth;
            if (updateRequest.RecurrenceInterval.HasValue) existing.RecurrenceInterval = updateRequest.RecurrenceInterval.Value;
            if (updateRequest.DailyStartTime.HasValue) existing.DailyStartTime = updateRequest.DailyStartTime;
            if (updateRequest.DailyEndTime.HasValue) existing.DailyEndTime = updateRequest.DailyEndTime;
            if (updateRequest.TimeZone != null) existing.TimeZone = updateRequest.TimeZone;
            if (updateRequest.EffectiveStartDate.HasValue) existing.EffectiveStartDate = updateRequest.EffectiveStartDate.Value;
            if (updateRequest.EffectiveEndDate.HasValue) existing.EffectiveEndDate = updateRequest.EffectiveEndDate;
            if (updateRequest.IsActive.HasValue) existing.IsActive = updateRequest.IsActive.Value;

            // Update schedule (includes validation and conflict detection)
            var updated = await _scheduleService.UpdateScheduleAsync(existing);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { schedule = updated });

            return response;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to update schedule {ScheduleId}", scheduleId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating schedule {ScheduleId}", scheduleId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to update schedule");
        }
    }

    /// <summary>
    /// DELETE /responders/{responderId}/schedules/{scheduleId}
    /// Deletes (deactivates) a schedule.
    /// </summary>
    [Function("DeleteResponderSchedule")]
    public async Task<HttpResponseData> DeleteSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "responders/{responderId}/schedules/{scheduleId}")]
        HttpRequestData req,
        string responderId,
        string scheduleId)
    {
        try
        {
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);
            var userId = JwtUtilities.GetUserIdFromClaims(claims);

            if (!Guid.TryParse(responderId, out var responderGuid) || !Guid.TryParse(scheduleId, out var scheduleGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid ID");
            }

            // Ensure user can delete schedule
            if (userId != responderGuid && !JwtUtilities.HasRole(claims, "hq") && !JwtUtilities.HasRole(claims, "admin"))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.Forbidden, "Cannot delete schedule");
            }

            // Verify schedule exists and belongs to responder
            var schedule = await _scheduleService.GetScheduleByIdAsync(scheduleGuid);
            if (schedule == null)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.NotFound, "Schedule not found");
            }

            if (schedule.ResponderId != responderGuid)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Schedule does not belong to this responder");
            }

            await _scheduleService.DeleteScheduleAsync(scheduleGuid);

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting schedule {ScheduleId}", scheduleId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to delete schedule");
        }
    }

    // ============================================
    // Availability & Preview Operations
    // ============================================

    /// <summary>
    /// GET /responders/{responderId}/availability
    /// Gets responder availability for a date range based on schedules.
    /// </summary>
    [Function("GetResponderAvailability")]
    public async Task<HttpResponseData> GetAvailability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "responders/{responderId}/availability")]
        HttpRequestData req,
        string responderId)
    {
        try
        {
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);

            if (!Guid.TryParse(responderId, out var responderGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid responder ID");
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];

            var startDate = !string.IsNullOrEmpty(startDateStr) ? DateTime.Parse(startDateStr) : DateTime.UtcNow;
            var endDate = !string.IsNullOrEmpty(endDateStr) ? DateTime.Parse(endDateStr) : DateTime.UtcNow.AddDays(7);

            // Limit to 90 days max
            if ((endDate - startDate).TotalDays > 90)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Date range cannot exceed 90 days");
            }

            var availability = await _scheduleService.CalculateAvailabilityAsync(responderGuid, startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                responderId = responderGuid,
                startDate = startDate,
                endDate = endDate,
                windows = availability,
                count = availability.Count
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting availability for responder {ResponderId}", responderId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to get availability");
        }
    }

    /// <summary>
    /// POST /responders/{responderId}/schedules/{scheduleId}/preview
    /// Generates a preview of when the schedule would be active.
    /// </summary>
    [Function("PreviewSchedule")]
    public async Task<HttpResponseData> PreviewSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/{responderId}/schedules/{scheduleId}/preview")]
        HttpRequestData req,
        string responderId,
        string scheduleId)
    {
        try
        {
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);

            if (!Guid.TryParse(scheduleId, out var scheduleGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid schedule ID");
            }

            var schedule = await _scheduleService.GetScheduleByIdAsync(scheduleGuid);
            if (schedule == null)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.NotFound, "Schedule not found");
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];

            var startDate = !string.IsNullOrEmpty(startDateStr) ? DateTime.Parse(startDateStr) : DateTime.UtcNow;
            var endDate = !string.IsNullOrEmpty(endDateStr) ? DateTime.Parse(endDateStr) : DateTime.UtcNow.AddDays(30);

            var preview = await _scheduleService.GenerateSchedulePreviewAsync(schedule, startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                schedule = new
                {
                    schedule.DesignationId,
                    schedule.Pattern,
                    schedule.DaysOfWeek,
                    schedule.DailyStartTime,
                    schedule.DailyEndTime,
                    schedule.TimeZone
                },
                preview = preview,
                count = preview.Count
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating preview for schedule {ScheduleId}", scheduleId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to generate preview");
        }
    }

    // ============================================
    // Exception & Override Operations
    // ============================================

    /// <summary>
    /// POST /responders/{responderId}/schedules/{scheduleId}/exceptions
    /// Adds exception dates to skip (vacations, holidays).
    /// </summary>
    [Function("AddScheduleException")]
    public async Task<HttpResponseData> AddException(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/{responderId}/schedules/{scheduleId}/exceptions")]
        HttpRequestData req,
        string responderId,
        string scheduleId)
    {
        try
        {
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);
            var userId = JwtUtilities.GetUserIdFromClaims(claims);

            if (!Guid.TryParse(responderId, out var responderGuid) || !Guid.TryParse(scheduleId, out var scheduleGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid ID");
            }

            if (userId != responderGuid && !JwtUtilities.HasRole(claims, "hq") && !JwtUtilities.HasRole(claims, "admin"))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.Forbidden, "Cannot modify schedule");
            }

            var body = await req.ReadAsStringAsync() ?? string.Empty;
            var request = JsonSerializer.Deserialize<AddExceptionRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || request.Date == default)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid date");
            }

            await _scheduleService.AddExceptionDateAsync(scheduleGuid, request.Date);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Exception date added", date = request.Date });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding exception to schedule {ScheduleId}", scheduleId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to add exception");
        }
    }

    /// <summary>
    /// POST /responders/{responderId}/schedules/{scheduleId}/overrides
    /// Adds or updates a schedule override for a specific date.
    /// </summary>
    [Function("AddScheduleOverride")]
    public async Task<HttpResponseData> AddOverride(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "responders/{responderId}/schedules/{scheduleId}/overrides")]
        HttpRequestData req,
        string responderId,
        string scheduleId)
    {
        try
        {
            var claims = JwtUtilities.ValidateJwtFromHeader(req.Headers);
            var userId = JwtUtilities.GetUserIdFromClaims(claims);

            if (!Guid.TryParse(responderId, out var responderGuid) || !Guid.TryParse(scheduleId, out var scheduleGuid))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid ID");
            }

            if (userId != responderGuid && !JwtUtilities.HasRole(claims, "hq") && !JwtUtilities.HasRole(claims, "admin"))
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.Forbidden, "Cannot modify schedule");
            }

            var body = await req.ReadAsStringAsync() ?? string.Empty;
            var request = JsonSerializer.Deserialize<AddOverrideRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || request.Date == default)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, "Invalid request");
            }

            var scheduleOverride = new ScheduleOverride
            {
                Date = request.Date,
                OverrideStartTime = request.OverrideStartTime,
                OverrideEndTime = request.OverrideEndTime,
                IsAvailable = request.IsAvailable ?? true
            };

            var created = await _scheduleService.AddOverrideAsync(scheduleGuid, scheduleOverride);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { scheduleOverride = created });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding override to schedule {ScheduleId}", scheduleId);
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to add override");
        }
    }
}

// ============================================
// Request/Response Models
// ============================================

public class CreateScheduleRequest
{
    public string? CommitmentType { get; set; }
    public double LocationLat { get; set; }
    public double LocationLng { get; set; }
    public string? LocationGeohash { get; set; }
    public string? LocationName { get; set; }
    public int RadiusMeters { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public RecurrencePattern Pattern { get; set; }
    public DaysOfWeek? DaysOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public int? RecurrenceInterval { get; set; }
    public TimeSpan? DailyStartTime { get; set; }
    public TimeSpan? DailyEndTime { get; set; }
    public string? TimeZone { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
}

public class UpdateScheduleRequest
{
    public string? CommitmentType { get; set; }
    public double? LocationLat { get; set; }
    public double? LocationLng { get; set; }
    public string? LocationGeohash { get; set; }
    public string? LocationName { get; set; }
    public int? RadiusMeters { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public RecurrencePattern? Pattern { get; set; }
    public DaysOfWeek? DaysOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public int? RecurrenceInterval { get; set; }
    public TimeSpan? DailyStartTime { get; set; }
    public TimeSpan? DailyEndTime { get; set; }
    public string? TimeZone { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
    public bool? IsActive { get; set; }
}

public class AddExceptionRequest
{
    public DateTime Date { get; set; }
}

public class AddOverrideRequest
{
    public DateTime Date { get; set; }
    public TimeSpan? OverrideStartTime { get; set; }
    public TimeSpan? OverrideEndTime { get; set; }
    public bool? IsAvailable { get; set; }
}

// Extension method for error responses
public static class HttpResponseDataExtensions
{
    public static async Task<HttpResponseData> CreateErrorResponseAsync(
        this HttpRequestData req,
        HttpStatusCode statusCode,
        string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}
