using System;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Scheduling;

/// <summary>
/// Generic OS Task Scheduler Service supporting Priority-Based, Earliest-Deadline-First (EDF), and Round-Robin scheduling policies. Ported from OS_Proof.
/// </summary>
public sealed class GenericOsTaskSchedulerService
{
    private readonly List<OsScheduledTask> _tasks = new();

    public void EnqueueTask(OsScheduledTask task)
    {
        _tasks.Add(task);
    }

    public List<TaskScheduleResult> DispatchSchedule(SchedulerAlgorithm algorithm)
    {
        var active = _tasks.Where(t => t.State == OsTaskState.Ready || t.State == OsTaskState.Running).ToList();

        var ordered = algorithm switch
        {
            SchedulerAlgorithm.PriorityBased => active.OrderByDescending(t => t.Priority).ThenBy(t => t.ScheduledTimeUtc),
            SchedulerAlgorithm.EarliestDeadlineFirst => active.OrderBy(t => t.DeadlineUtc ?? DateTime.MaxValue).ThenByDescending(t => t.Priority),
            SchedulerAlgorithm.RoundRobin => active.OrderBy(t => t.ScheduledTimeUtc),
            SchedulerAlgorithm.FirstComeFirstServed => active.OrderBy(t => t.ScheduledTimeUtc),
            _ => active.OrderByDescending(t => t.Priority)
        };

        var results = new List<TaskScheduleResult>();
        int order = 1;

        foreach (var task in ordered)
        {
            results.Add(new TaskScheduleResult(
                TaskId: task.TaskId,
                ExecutionOrder: order++,
                DispatchedAtUtc: DateTime.UtcNow,
                AssignedWorker: $"worker-core-{(order % 4) + 1}"
            ));
        }

        return results;
    }
}
