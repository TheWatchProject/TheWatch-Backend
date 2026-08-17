using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Data.Repositories;

/// <summary>
/// Repository implementation for designated responder schedule operations.
/// </summary>
public class ResponderScheduleRepository : IResponderScheduleRepository
{
    private readonly WatchDbContext _context;

    public ResponderScheduleRepository(WatchDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // ============================================
    // CRUD Operations
    // ============================================

    public async Task<DesignatedResponderSchedule> CreateScheduleAsync(DesignatedResponderSchedule schedule)
    {
        _context.DesignatedResponderSchedules.Add(schedule);
        await _context.SaveChangesAsync();
        return schedule;
    }

    public async Task UpdateScheduleAsync(DesignatedResponderSchedule schedule)
    {
        schedule.UpdatedAt = DateTime.UtcNow;
        _context.DesignatedResponderSchedules.Update(schedule);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteScheduleAsync(Guid scheduleId)
    {
        var schedule = await _context.DesignatedResponderSchedules
            .FirstOrDefaultAsync(s => s.DesignationId == scheduleId);

        if (schedule != null)
        {
            schedule.IsActive = false;
            schedule.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<DesignatedResponderSchedule?> GetScheduleByIdAsync(Guid scheduleId)
    {
        return await _context.DesignatedResponderSchedules
            .Include(s => s.Overrides)
            .FirstOrDefaultAsync(s => s.DesignationId == scheduleId);
    }

    public async Task<List<DesignatedResponderSchedule>> GetSchedulesByResponderIdAsync(
        Guid responderId,
        bool includeInactive = false)
    {
        var query = _context.DesignatedResponderSchedules
            .Include(s => s.Overrides)
            .Where(s => s.ResponderId == responderId);

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        return await query
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
    }

    // ============================================
    // Conflict Detection
    // ============================================

    public async Task<List<DesignatedResponderSchedule>> GetConflictingSchedulesAsync(DesignatedResponderSchedule schedule)
    {
        // Get all active schedules for the same responder (excluding the current schedule if updating)
        var existingSchedules = await _context.DesignatedResponderSchedules
            .Where(s => s.ResponderId == schedule.ResponderId &&
                       s.IsActive &&
                       s.DesignationId != schedule.DesignationId)
            .ToListAsync();

        var conflicts = new List<DesignatedResponderSchedule>();

        foreach (var existing in existingSchedules)
        {
            if (DoSchedulesOverlap(schedule, existing))
            {
                conflicts.Add(existing);
            }
        }

        return conflicts;
    }

    public async Task<bool> HasConflictsAsync(DesignatedResponderSchedule schedule)
    {
        var conflicts = await GetConflictingSchedulesAsync(schedule);
        return conflicts.Any();
    }

    /// <summary>
    /// Determines if two schedules have overlapping time windows.
    /// </summary>
    private bool DoSchedulesOverlap(DesignatedResponderSchedule schedule1, DesignatedResponderSchedule schedule2)
    {
        // Check date range overlap
        if (schedule1.EffectiveEndDate.HasValue && schedule2.EffectiveStartDate > schedule1.EffectiveEndDate.Value)
            return false;

        if (schedule2.EffectiveEndDate.HasValue && schedule1.EffectiveStartDate > schedule2.EffectiveEndDate.Value)
            return false;

        // For one-time schedules, check exact datetime overlap
        if (schedule1.CommitmentType == "one_time" && schedule2.CommitmentType == "one_time")
        {
            if (!schedule1.StartTime.HasValue || !schedule1.EndTime.HasValue ||
                !schedule2.StartTime.HasValue || !schedule2.EndTime.HasValue)
                return false;

            return schedule1.StartTime < schedule2.EndTime && schedule1.EndTime > schedule2.StartTime;
        }

        // For recurring schedules, check if they could overlap on any day
        if (schedule1.Pattern == RecurrencePattern.Weekly && schedule2.Pattern == RecurrencePattern.Weekly)
        {
            // Check if any days of week overlap
            if (schedule1.DaysOfWeek.HasValue && schedule2.DaysOfWeek.HasValue)
            {
                var overlappingDays = schedule1.DaysOfWeek.Value & schedule2.DaysOfWeek.Value;
                if (overlappingDays == Core.Entities.DaysOfWeek.None)
                    return false;
            }
        }

        // Check time of day overlap
        if (schedule1.DailyStartTime.HasValue && schedule1.DailyEndTime.HasValue &&
            schedule2.DailyStartTime.HasValue && schedule2.DailyEndTime.HasValue)
        {
            return schedule1.DailyStartTime < schedule2.DailyEndTime &&
                   schedule1.DailyEndTime > schedule2.DailyStartTime;
        }

        // If we can't determine definitively, assume potential conflict (conservative approach)
        return true;
    }

    // ============================================
    // Availability Queries
    // ============================================

    public async Task<List<DesignatedResponderSchedule>> GetActiveSchedulesForDateTimeAsync(DateTime datetime)
    {
        var date = datetime.Date;

        // Get schedules that could be active at this datetime
        var schedules = await _context.DesignatedResponderSchedules
            .Include(s => s.Overrides)
            .Where(s => s.IsActive &&
                       s.EffectiveStartDate <= date &&
                       (!s.EffectiveEndDate.HasValue || s.EffectiveEndDate.Value >= date))
            .ToListAsync();

        // Filter by commitment type and time
        return schedules.Where(s => IsScheduleActiveAtDateTime(s, datetime)).ToList();
    }

    public async Task<List<DesignatedResponderSchedule>> GetActiveSchedulesForDateAsync(DateTime date)
    {
        date = date.Date;

        return await _context.DesignatedResponderSchedules
            .Include(s => s.Overrides)
            .Where(s => s.IsActive &&
                       s.EffectiveStartDate <= date &&
                       (!s.EffectiveEndDate.HasValue || s.EffectiveEndDate.Value >= date))
            .ToListAsync();
    }

    public async Task<List<DesignatedResponderSchedule>> GetSchedulesForDateRangeAsync(
        Guid responderId,
        DateTime startDate,
        DateTime endDate)
    {
        startDate = startDate.Date;
        endDate = endDate.Date;

        return await _context.DesignatedResponderSchedules
            .Include(s => s.Overrides)
            .Where(s => s.ResponderId == responderId &&
                       s.IsActive &&
                       s.EffectiveStartDate <= endDate &&
                       (!s.EffectiveEndDate.HasValue || s.EffectiveEndDate.Value >= startDate))
            .ToListAsync();
    }

    /// <summary>
    /// Helper method to check if a schedule is active at a specific datetime.
    /// </summary>
    private bool IsScheduleActiveAtDateTime(DesignatedResponderSchedule schedule, DateTime datetime)
    {
        var date = datetime.Date;
        var timeOfDay = datetime.TimeOfDay;

        // Check if date is in exception list
        if (!string.IsNullOrEmpty(schedule.ExceptionDatesJson))
        {
            // Parse exception dates and check if current date is excluded
            // TODO: Implement JSON parsing when service is available
        }

        // Check for date-specific override
        var dateOverride = schedule.Overrides.FirstOrDefault(o => o.Date.Date == date);
        if (dateOverride != null)
        {
            if (!dateOverride.IsAvailable)
                return false;

            if (dateOverride.OverrideStartTime.HasValue && dateOverride.OverrideEndTime.HasValue)
            {
                return timeOfDay >= dateOverride.OverrideStartTime.Value &&
                       timeOfDay <= dateOverride.OverrideEndTime.Value;
            }
        }

        // Check commitment type
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
                return IsRecurringScheduleActive(schedule, datetime);

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if a recurring schedule is active at a specific datetime.
    /// </summary>
    private bool IsRecurringScheduleActive(DesignatedResponderSchedule schedule, DateTime datetime)
    {
        var timeOfDay = datetime.TimeOfDay;

        // Check time window
        if (schedule.DailyStartTime.HasValue && schedule.DailyEndTime.HasValue)
        {
            if (timeOfDay < schedule.DailyStartTime.Value || timeOfDay > schedule.DailyEndTime.Value)
                return false;
        }

        // Check recurrence pattern
        switch (schedule.Pattern)
        {
            case RecurrencePattern.Daily:
                // Check if it's every N days from start date
                var daysSinceStart = (datetime.Date - schedule.EffectiveStartDate).Days;
                return daysSinceStart % schedule.RecurrenceInterval == 0;

            case RecurrencePattern.Weekly:
                if (!schedule.DaysOfWeek.HasValue)
                    return false;

                var dayOfWeekFlag = ConvertDayOfWeekToFlag(datetime.DayOfWeek);
                var isMatchingDay = schedule.DaysOfWeek.Value.HasFlag(dayOfWeekFlag);

                // Check if it's the right week (every N weeks)
                if (schedule.RecurrenceInterval > 1)
                {
                    var weeksSinceStart = (datetime.Date - schedule.EffectiveStartDate).Days / 7;
                    return isMatchingDay && (weeksSinceStart % schedule.RecurrenceInterval == 0);
                }

                return isMatchingDay;

            case RecurrencePattern.Monthly:
                if (!schedule.DayOfMonth.HasValue)
                    return false;

                return datetime.Day == schedule.DayOfMonth.Value;

            case RecurrencePattern.Custom:
                // TODO: Implement custom pattern parsing
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Converts System.DayOfWeek to DaysOfWeek flag enum.
    /// </summary>
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

    // ============================================
    // Schedule Overrides
    // ============================================

    public async Task<ScheduleOverride> CreateOverrideAsync(ScheduleOverride scheduleOverride)
    {
        scheduleOverride.Id = Guid.NewGuid();
        scheduleOverride.CreatedAt = DateTime.UtcNow;
        _context.Set<ScheduleOverride>().Add(scheduleOverride);
        await _context.SaveChangesAsync();
        return scheduleOverride;
    }

    public async Task UpdateOverrideAsync(ScheduleOverride scheduleOverride)
    {
        _context.Set<ScheduleOverride>().Update(scheduleOverride);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteOverrideAsync(Guid overrideId)
    {
        var scheduleOverride = await _context.Set<ScheduleOverride>()
            .FirstOrDefaultAsync(o => o.Id == overrideId);

        if (scheduleOverride != null)
        {
            _context.Set<ScheduleOverride>().Remove(scheduleOverride);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<ScheduleOverride>> GetOverridesForScheduleAsync(Guid scheduleId)
    {
        return await _context.Set<ScheduleOverride>()
            .Where(o => o.ScheduleId == scheduleId)
            .OrderBy(o => o.Date)
            .ToListAsync();
    }

    public async Task<ScheduleOverride?> GetOverrideForDateAsync(Guid scheduleId, DateTime date)
    {
        date = date.Date;
        return await _context.Set<ScheduleOverride>()
            .FirstOrDefaultAsync(o => o.ScheduleId == scheduleId && o.Date.Date == date);
    }

    // ============================================
    // Bulk Operations
    // ============================================

    public async Task<List<Guid>> GetAvailableResponderIdsAsync()
    {
        var now = DateTime.UtcNow;
        var activeSchedules = await GetActiveSchedulesForDateTimeAsync(now);
        return activeSchedules.Select(s => s.ResponderId).Distinct().ToList();
    }

    public async Task<List<DesignatedResponderSchedule>> GetTransitioningSchedulesAsync(int minutesAhead = 5)
    {
        var now = DateTime.UtcNow;
        var futureTime = now.AddMinutes(minutesAhead);

        // Get all active schedules
        var schedules = await _context.DesignatedResponderSchedules
            .Include(s => s.Overrides)
            .Where(s => s.IsActive)
            .ToListAsync();

        // Filter to schedules that are starting or ending within the time window
        var transitioning = new List<DesignatedResponderSchedule>();

        foreach (var schedule in schedules)
        {
            var isActiveNow = IsScheduleActiveAtDateTime(schedule, now);
            var willBeActiveFuture = IsScheduleActiveAtDateTime(schedule, futureTime);

            // If status is changing (starting or ending), include it
            if (isActiveNow != willBeActiveFuture)
            {
                transitioning.Add(schedule);
            }
        }

        return transitioning;
    }
}
