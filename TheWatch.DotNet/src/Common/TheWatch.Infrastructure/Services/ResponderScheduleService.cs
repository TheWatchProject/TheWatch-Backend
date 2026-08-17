using Microsoft.Extensions.Logging;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Service implementation for designated responder scheduling business logic.
/// </summary>
public class ResponderScheduleService : IResponderScheduleService
{
    private readonly IResponderScheduleRepository _scheduleRepository;
    private readonly IResponderOnboardingRepository _responderRepository;
    private readonly ILogger<ResponderScheduleService> _logger;

    public ResponderScheduleService(
        IResponderScheduleRepository scheduleRepository,
        IResponderOnboardingRepository responderRepository,
        ILogger<ResponderScheduleService> logger)
    {
        _scheduleRepository = scheduleRepository ?? throw new ArgumentNullException(nameof(scheduleRepository));
        _responderRepository = responderRepository ?? throw new ArgumentNullException(nameof(responderRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ============================================
    // Schedule Management
    // ============================================

    public async Task<DesignatedResponderSchedule> CreateScheduleAsync(DesignatedResponderSchedule schedule)
    {
        // Validate schedule
        var validationResult = await ValidateScheduleAsync(schedule);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Schedule validation failed: {string.Join(", ", validationResult.Errors)}");
        }

        // Check for conflicts
        var conflicts = await CheckConflictsAsync(schedule);
        if (conflicts.Any())
        {
            throw new InvalidOperationException(
                $"Schedule conflicts with {conflicts.Count} existing schedule(s). " +
                $"Conflicting schedule IDs: {string.Join(", ", conflicts.Select(c => c.DesignationId))}");
        }

        // Set timestamps
        schedule.DesignationId = Guid.NewGuid();
        schedule.CreatedAt = DateTime.UtcNow;
        schedule.UpdatedAt = DateTime.UtcNow;

        var created = await _scheduleRepository.CreateScheduleAsync(schedule);

        _logger.LogInformation(
            "Created schedule {ScheduleId} for responder {ResponderId}",
            created.DesignationId,
            created.ResponderId);

        return created;
    }

    public async Task<DesignatedResponderSchedule> UpdateScheduleAsync(DesignatedResponderSchedule schedule)
    {
        // Validate schedule
        var validationResult = await ValidateScheduleAsync(schedule);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Schedule validation failed: {string.Join(", ", validationResult.Errors)}");
        }

        // Check for conflicts (excluding current schedule)
        var conflicts = await CheckConflictsAsync(schedule);
        if (conflicts.Any())
        {
            throw new InvalidOperationException(
                $"Schedule conflicts with {conflicts.Count} existing schedule(s). " +
                $"Conflicting schedule IDs: {string.Join(", ", conflicts.Select(c => c.DesignationId))}");
        }

        await _scheduleRepository.UpdateScheduleAsync(schedule);

        _logger.LogInformation(
            "Updated schedule {ScheduleId} for responder {ResponderId}",
            schedule.DesignationId,
            schedule.ResponderId);

        return schedule;
    }

    public async Task DeleteScheduleAsync(Guid scheduleId)
    {
        await _scheduleRepository.DeleteScheduleAsync(scheduleId);
        _logger.LogInformation("Deleted schedule {ScheduleId}", scheduleId);
    }

    public async Task<DesignatedResponderSchedule?> GetScheduleByIdAsync(Guid scheduleId)
    {
        return await _scheduleRepository.GetScheduleByIdAsync(scheduleId);
    }

    public async Task<List<DesignatedResponderSchedule>> GetSchedulesByResponderIdAsync(
        Guid responderId,
        bool includeInactive = false)
    {
        return await _scheduleRepository.GetSchedulesByResponderIdAsync(responderId, includeInactive);
    }

    // ============================================
    // Validation & Conflict Detection
    // ============================================

    public async Task<ValidationResult> ValidateScheduleAsync(DesignatedResponderSchedule schedule)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check responder exists and is eligible
        var responder = await _responderRepository.GetResponderProfileAsync(schedule.ResponderId);
        if (responder == null)
        {
            errors.Add("Responder profile not found");
        }
        else if (!responder.IsResponderEligible)
        {
            errors.Add("Responder is not eligible to accept assignments");
        }

        // Validate commitment type
        if (schedule.CommitmentType != "recurring" &&
            schedule.CommitmentType != "one_time" &&
            schedule.CommitmentType != "ongoing")
        {
            errors.Add("Invalid commitment type. Must be: recurring, one_time, or ongoing");
        }

        // Validate one_time schedules
        if (schedule.CommitmentType == "one_time")
        {
            if (!schedule.StartTime.HasValue || !schedule.EndTime.HasValue)
            {
                errors.Add("One-time schedules must have StartTime and EndTime");
            }
            else if (schedule.StartTime >= schedule.EndTime)
            {
                errors.Add("StartTime must be before EndTime");
            }
        }

        // Validate recurring schedules
        if (schedule.CommitmentType == "recurring")
        {
            if (schedule.Pattern == RecurrencePattern.None)
            {
                errors.Add("Recurring schedules must specify a recurrence pattern");
            }

            if (!schedule.DailyStartTime.HasValue || !schedule.DailyEndTime.HasValue)
            {
                errors.Add("Recurring schedules must have DailyStartTime and DailyEndTime");
            }
            else if (schedule.DailyStartTime >= schedule.DailyEndTime)
            {
                errors.Add("DailyStartTime must be before DailyEndTime");
            }

            // Validate pattern-specific requirements
            switch (schedule.Pattern)
            {
                case RecurrencePattern.Weekly:
                    if (!schedule.DaysOfWeek.HasValue || schedule.DaysOfWeek == Core.Entities.DaysOfWeek.None)
                    {
                        errors.Add("Weekly schedules must specify at least one day of week");
                    }
                    break;

                case RecurrencePattern.Monthly:
                    if (!schedule.DayOfMonth.HasValue || schedule.DayOfMonth < 1 || schedule.DayOfMonth > 31)
                    {
                        errors.Add("Monthly schedules must specify a valid day of month (1-31)");
                    }
                    break;
            }

            if (schedule.RecurrenceInterval < 1)
            {
                errors.Add("Recurrence interval must be at least 1");
            }
        }

        // Validate date ranges
        if (schedule.EffectiveEndDate.HasValue &&
            schedule.EffectiveEndDate.Value < schedule.EffectiveStartDate)
        {
            errors.Add("EffectiveEndDate must be after EffectiveStartDate");
        }

        // Validate location
        if (schedule.LocationLat < -90 || schedule.LocationLat > 90)
        {
            errors.Add("Invalid latitude (must be -90 to 90)");
        }

        if (schedule.LocationLng < -180 || schedule.LocationLng > 180)
        {
            errors.Add("Invalid longitude (must be -180 to 180)");
        }

        if (schedule.RadiusMeters < 100 || schedule.RadiusMeters > 50000)
        {
            warnings.Add("Radius should typically be between 100m and 50km");
        }

        // Validate timezone
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        }
        catch
        {
            errors.Add($"Invalid timezone identifier: {schedule.TimeZone}");
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors,
            Warnings = warnings
        };
    }

    public async Task<List<DesignatedResponderSchedule>> CheckConflictsAsync(DesignatedResponderSchedule schedule)
    {
        return await _scheduleRepository.GetConflictingSchedulesAsync(schedule);
    }

    // ============================================
    // Recurrence & Availability Calculation
    // ============================================

    public async Task<List<AvailabilityWindow>> GenerateSchedulePreviewAsync(
        DesignatedResponderSchedule schedule,
        DateTime startDate,
        DateTime endDate)
    {
        var windows = new List<AvailabilityWindow>();

        // Normalize to dates only
        startDate = startDate.Date;
        endDate = endDate.Date;

        // Limit preview to reasonable range (1 year max)
        if ((endDate - startDate).TotalDays > 365)
        {
            endDate = startDate.AddYears(1);
        }

        // Generate windows based on commitment type
        switch (schedule.CommitmentType)
        {
            case "ongoing":
                windows.Add(CreateAvailabilityWindow(schedule, startDate, endDate));
                break;

            case "one_time":
                if (schedule.StartTime.HasValue && schedule.EndTime.HasValue)
                {
                    var start = schedule.StartTime.Value;
                    var end = schedule.EndTime.Value;

                    if (start.Date >= startDate && start.Date <= endDate)
                    {
                        windows.Add(CreateAvailabilityWindow(schedule, start, end));
                    }
                }
                break;

            case "recurring":
                windows.AddRange(GenerateRecurringWindows(schedule, startDate, endDate));
                break;
        }

        return windows;
    }

    private List<AvailabilityWindow> GenerateRecurringWindows(
        DesignatedResponderSchedule schedule,
        DateTime startDate,
        DateTime endDate)
    {
        var windows = new List<AvailabilityWindow>();

        // Get exception dates
        var exceptionDates = ParseExceptionDates(schedule.ExceptionDatesJson);

        // Iterate through date range
        var currentDate = startDate;
        while (currentDate <= endDate)
        {
            // Check if date is in effective range
            if (currentDate >= schedule.EffectiveStartDate &&
                (!schedule.EffectiveEndDate.HasValue || currentDate <= schedule.EffectiveEndDate.Value))
            {
                // Check if date is an exception
                if (!exceptionDates.Contains(currentDate.Date))
                {
                    // Check for override
                    var scheduleOverride = schedule.Overrides.FirstOrDefault(o => o.Date.Date == currentDate.Date);

                    if (scheduleOverride != null)
                    {
                        if (scheduleOverride.IsAvailable &&
                            scheduleOverride.OverrideStartTime.HasValue &&
                            scheduleOverride.OverrideEndTime.HasValue)
                        {
                            var start = currentDate.Add(scheduleOverride.OverrideStartTime.Value);
                            var end = currentDate.Add(scheduleOverride.OverrideEndTime.Value);
                            windows.Add(CreateAvailabilityWindow(schedule, start, end));
                        }
                    }
                    else if (IsDateMatchingPattern(schedule, currentDate))
                    {
                        // No override, check if date matches recurrence pattern
                        if (schedule.DailyStartTime.HasValue && schedule.DailyEndTime.HasValue)
                        {
                            var start = currentDate.Add(schedule.DailyStartTime.Value);
                            var end = currentDate.Add(schedule.DailyEndTime.Value);
                            windows.Add(CreateAvailabilityWindow(schedule, start, end));
                        }
                    }
                }
            }

            currentDate = currentDate.AddDays(1);
        }

        return windows;
    }

    private bool IsDateMatchingPattern(DesignatedResponderSchedule schedule, DateTime date)
    {
        switch (schedule.Pattern)
        {
            case RecurrencePattern.Daily:
                var daysSinceStart = (date - schedule.EffectiveStartDate).Days;
                return daysSinceStart >= 0 && daysSinceStart % schedule.RecurrenceInterval == 0;

            case RecurrencePattern.Weekly:
                if (!schedule.DaysOfWeek.HasValue)
                    return false;

                var dayOfWeekFlag = ConvertDayOfWeekToFlag(date.DayOfWeek);
                var isMatchingDay = schedule.DaysOfWeek.Value.HasFlag(dayOfWeekFlag);

                if (schedule.RecurrenceInterval > 1)
                {
                    var weeksSinceStart = (date - schedule.EffectiveStartDate).Days / 7;
                    return isMatchingDay && (weeksSinceStart % schedule.RecurrenceInterval == 0);
                }

                return isMatchingDay;

            case RecurrencePattern.Monthly:
                if (!schedule.DayOfMonth.HasValue)
                    return false;

                // Handle months with fewer days (e.g., Feb 31 -> last day of Feb)
                var targetDay = Math.Min(schedule.DayOfMonth.Value, DateTime.DaysInMonth(date.Year, date.Month));
                return date.Day == targetDay;

            default:
                return false;
        }
    }

    public async Task<List<AvailabilityWindow>> CalculateAvailabilityAsync(
        Guid responderId,
        DateTime startDate,
        DateTime endDate)
    {
        var schedules = await _scheduleRepository.GetSchedulesForDateRangeAsync(responderId, startDate, endDate);
        var allWindows = new List<AvailabilityWindow>();

        foreach (var schedule in schedules)
        {
            var windows = await GenerateSchedulePreviewAsync(schedule, startDate, endDate);
            allWindows.AddRange(windows);
        }

        // Sort by start time
        return allWindows.OrderBy(w => w.StartTime).ToList();
    }

    public async Task<bool> IsScheduleActiveAtAsync(DesignatedResponderSchedule schedule, DateTime datetime)
    {
        var date = datetime.Date;

        // Check effective date range
        if (date < schedule.EffectiveStartDate ||
            (schedule.EffectiveEndDate.HasValue && date > schedule.EffectiveEndDate.Value))
        {
            return false;
        }

        // Check exception dates
        var exceptionDates = ParseExceptionDates(schedule.ExceptionDatesJson);
        if (exceptionDates.Contains(date))
        {
            return false;
        }

        // Check for override
        var scheduleOverride = schedule.Overrides.FirstOrDefault(o => o.Date.Date == date);
        if (scheduleOverride != null)
        {
            if (!scheduleOverride.IsAvailable)
                return false;

            if (scheduleOverride.OverrideStartTime.HasValue && scheduleOverride.OverrideEndTime.HasValue)
            {
                var timeOfDay = datetime.TimeOfDay;
                return timeOfDay >= scheduleOverride.OverrideStartTime.Value &&
                       timeOfDay <= scheduleOverride.OverrideEndTime.Value;
            }
        }

        // Check based on commitment type
        switch (schedule.CommitmentType)
        {
            case "ongoing":
                return true;

            case "one_time":
                if (schedule.StartTime.HasValue && schedule.EndTime.HasValue)
                {
                    return datetime >= schedule.StartTime.Value && datetime <= schedule.EndTime.Value;
                }
                return false;

            case "recurring":
                if (!IsDateMatchingPattern(schedule, date))
                    return false;

                if (schedule.DailyStartTime.HasValue && schedule.DailyEndTime.HasValue)
                {
                    var timeOfDay = datetime.TimeOfDay;
                    return timeOfDay >= schedule.DailyStartTime.Value &&
                           timeOfDay <= schedule.DailyEndTime.Value;
                }
                return false;

            default:
                return false;
        }
    }

    public async Task<bool> IsResponderAvailableAtAsync(Guid responderId, DateTime datetime)
    {
        var schedules = await _scheduleRepository.GetSchedulesByResponderIdAsync(responderId);

        foreach (var schedule in schedules)
        {
            if (await IsScheduleActiveAtAsync(schedule, datetime))
            {
                return true;
            }
        }

        return false;
    }

    // ============================================
    // Schedule Overrides & Exceptions
    // ============================================

    public async Task AddExceptionDateAsync(Guid scheduleId, DateTime date)
    {
        var schedule = await _scheduleRepository.GetScheduleByIdAsync(scheduleId);
        if (schedule == null)
        {
            throw new InvalidOperationException($"Schedule {scheduleId} not found");
        }

        var exceptionDates = ParseExceptionDates(schedule.ExceptionDatesJson).ToList();
        var dateOnly = date.Date;

        if (!exceptionDates.Contains(dateOnly))
        {
            exceptionDates.Add(dateOnly);
            schedule.ExceptionDatesJson = SerializeExceptionDates(exceptionDates);
            await _scheduleRepository.UpdateScheduleAsync(schedule);

            _logger.LogInformation(
                "Added exception date {Date} to schedule {ScheduleId}",
                dateOnly,
                scheduleId);
        }
    }

    public async Task RemoveExceptionDateAsync(Guid scheduleId, DateTime date)
    {
        var schedule = await _scheduleRepository.GetScheduleByIdAsync(scheduleId);
        if (schedule == null)
        {
            throw new InvalidOperationException($"Schedule {scheduleId} not found");
        }

        var exceptionDates = ParseExceptionDates(schedule.ExceptionDatesJson).ToList();
        var dateOnly = date.Date;

        if (exceptionDates.Remove(dateOnly))
        {
            schedule.ExceptionDatesJson = SerializeExceptionDates(exceptionDates);
            await _scheduleRepository.UpdateScheduleAsync(schedule);

            _logger.LogInformation(
                "Removed exception date {Date} from schedule {ScheduleId}",
                dateOnly,
                scheduleId);
        }
    }

    public async Task<ScheduleOverride> AddOverrideAsync(Guid scheduleId, ScheduleOverride scheduleOverride)
    {
        var schedule = await _scheduleRepository.GetScheduleByIdAsync(scheduleId);
        if (schedule == null)
        {
            throw new InvalidOperationException($"Schedule {scheduleId} not found");
        }

        // Check if override already exists for this date
        var existing = await _scheduleRepository.GetOverrideForDateAsync(scheduleId, scheduleOverride.Date);
        if (existing != null)
        {
            // Update existing override
            existing.OverrideStartTime = scheduleOverride.OverrideStartTime;
            existing.OverrideEndTime = scheduleOverride.OverrideEndTime;
            existing.IsAvailable = scheduleOverride.IsAvailable;
            await _scheduleRepository.UpdateOverrideAsync(existing);
            return existing;
        }

        // Create new override
        scheduleOverride.ScheduleId = scheduleId;
        return await _scheduleRepository.CreateOverrideAsync(scheduleOverride);
    }

    public async Task RemoveOverrideAsync(Guid scheduleId, Guid overrideId)
    {
        await _scheduleRepository.DeleteOverrideAsync(overrideId);
        _logger.LogInformation(
            "Removed override {OverrideId} from schedule {ScheduleId}",
            overrideId,
            scheduleId);
    }

    // ============================================
    // Background Job Support
    // ============================================

    public async Task<int> SyncAvailabilityStatusAsync()
    {
        var now = DateTime.UtcNow;
        var updateCount = 0;

        _logger.LogInformation("Starting responder availability sync at {Time}", now);

        try
        {
            // Get all active schedules at current time
            var activeSchedules = await _scheduleRepository.GetActiveSchedulesForDateTimeAsync(now);
            var activeResponderIds = activeSchedules.Select(s => s.ResponderId).Distinct().ToList();

            // Update responders who should be available
            foreach (var responderId in activeResponderIds)
            {
                var responder = await _responderRepository.GetResponderProfileAsync(responderId);
                if (responder != null && responder.CurrentStatus != "available" && responder.CurrentStatus != "on_duty")
                {
                    responder.CurrentStatus = "on_duty";
                    responder.AvailableUntil = now.AddHours(24); // Default 24-hour availability
                    await _responderRepository.UpdateResponderProfileAsync(responder);
                    updateCount++;

                    _logger.LogInformation(
                        "Updated responder {ResponderId} status to on_duty",
                        responderId);
                }
            }

            // Get transitioning schedules (ending soon)
            var transitioningSchedules = await _scheduleRepository.GetTransitioningSchedulesAsync(5);
            var endingResponderIds = transitioningSchedules
                .Where(s => !activeSchedules.Any(a => a.DesignationId == s.DesignationId))
                .Select(s => s.ResponderId)
                .Distinct()
                .ToList();

            // Update responders whose schedules are ending
            foreach (var responderId in endingResponderIds)
            {
                // Check if responder has any other active schedules
                var hasOtherActiveSchedules = activeSchedules.Any(s => s.ResponderId == responderId);
                if (!hasOtherActiveSchedules)
                {
                    var responder = await _responderRepository.GetResponderProfileAsync(responderId);
                    if (responder != null && responder.CurrentStatus == "on_duty")
                    {
                        responder.CurrentStatus = "unavailable";
                        responder.AvailableUntil = null;
                        await _responderRepository.UpdateResponderProfileAsync(responder);
                        updateCount++;

                        _logger.LogInformation(
                            "Updated responder {ResponderId} status to unavailable (schedule ended)",
                            responderId);
                    }
                }
            }

            _logger.LogInformation(
                "Responder availability sync completed. Updated {Count} responders",
                updateCount);

            return updateCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during responder availability sync");
            throw;
        }
    }

    public async Task UpdateResponderAvailabilityAsync(Guid responderId)
    {
        var now = DateTime.UtcNow;
        var isAvailable = await IsResponderAvailableAtAsync(responderId, now);

        var responder = await _responderRepository.GetResponderProfileAsync(responderId);
        if (responder != null)
        {
            var newStatus = isAvailable ? "on_duty" : "unavailable";
            if (responder.CurrentStatus != newStatus)
            {
                responder.CurrentStatus = newStatus;
                responder.AvailableUntil = isAvailable ? now.AddHours(24) : null;
                await _responderRepository.UpdateResponderProfileAsync(responder);

                _logger.LogInformation(
                    "Updated responder {ResponderId} status to {Status}",
                    responderId,
                    newStatus);
            }
        }
    }

    // ============================================
    // Timezone Support
    // ============================================

    public DateTime ConvertToScheduleTimezone(DateTime utcDateTime, string timezone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, tz);
    }

    public DateTime ConvertToUtc(DateTime localDateTime, string timezone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        return TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
    }

    // ============================================
    // Helper Methods
    // ============================================

    private AvailabilityWindow CreateAvailabilityWindow(
        DesignatedResponderSchedule schedule,
        DateTime startTime,
        DateTime endTime)
    {
        return new AvailabilityWindow
        {
            StartTime = startTime,
            EndTime = endTime,
            ScheduleId = schedule.DesignationId,
            LocationName = schedule.LocationName,
            LocationLat = schedule.LocationLat,
            LocationLng = schedule.LocationLng,
            RadiusMeters = schedule.RadiusMeters
        };
    }

    private Core.Entities.DaysOfWeek ConvertDayOfWeekToFlag(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => Core.Entities.DaysOfWeek.Monday,
            DayOfWeek.Tuesday => Core.Entities.DaysOfWeek.Tuesday,
            DayOfWeek.Wednesday => Core.Entities.DaysOfWeek.Wednesday,
            DayOfWeek.Thursday => Core.Entities.DaysOfWeek.Thursday,
            DayOfWeek.Friday => Core.Entities.DaysOfWeek.Friday,
            DayOfWeek.Saturday => Core.Entities.DaysOfWeek.Saturday,
            DayOfWeek.Sunday => Core.Entities.DaysOfWeek.Sunday,
            _ => Core.Entities.DaysOfWeek.None
        };
    }

    private HashSet<DateTime> ParseExceptionDates(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new HashSet<DateTime>();

        try
        {
            var dates = JsonSerializer.Deserialize<List<DateTime>>(json);
            return dates?.Select(d => d.Date).ToHashSet() ?? new HashSet<DateTime>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse exception dates JSON");
            return new HashSet<DateTime>();
        }
    }

    private string SerializeExceptionDates(List<DateTime> dates)
    {
        var datesOnly = dates.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
        return JsonSerializer.Serialize(datesOnly);
    }
}
