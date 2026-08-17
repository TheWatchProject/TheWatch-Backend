using System;
using System.Collections.Generic;
using TheWatch.Contracts;

namespace TheWatch.Geospatial.Db;

/// <summary>
/// Evaluates whether a tactical responder is on-call at a given instant from their AvailabilitySchedule.
/// Precedence: "Available Now" instant override -> Date-specific override (block beats allow) -> Weekly recurring window.
/// </summary>
public static class AvailabilityEvaluator
{
    public static bool IsAvailableAt(AvailabilitySchedule schedule, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        // 1. Available Now instant override
        if (schedule.AvailableNow &&
            (schedule.AvailableNowUntilUtc is null || instant.UtcDateTime <= schedule.AvailableNowUntilUtc))
        {
            return true;
        }

        var date = DateOnly.FromDateTime(instant.DateTime);
        var hour = instant.Hour;

        // 2. Date-specific override (explicit block beats an allow)
        bool? overridden = null;
        foreach (var o in schedule.Overrides)
        {
            if (o.Date != date || !o.CoversHour(hour)) continue;
            if (!o.IsAvailable) return false;
            overridden = true;
        }

        if (overridden == true) return true;

        // 3. Weekly recurring window
        foreach (var w in schedule.Weekly)
        {
            if (w.Day == instant.DayOfWeek && w.CoversHour(hour))
                return true;
        }

        return false;
    }

    public static bool IsOnCallNow(AvailabilitySchedule schedule, DateTimeOffset? now = null)
        => IsAvailableAt(schedule, now ?? DateTimeOffset.UtcNow);

    public static int WeeklyOnCallHours(AvailabilitySchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var slots = new HashSet<(DayOfWeek, int)>();
        foreach (var w in schedule.Weekly)
        {
            for (var h = Math.Max(0, w.StartHour); h < Math.Min(24, w.EndHour); h++)
            {
                slots.Add((w.Day, h));
            }
        }
        return slots.Count;
    }
}
