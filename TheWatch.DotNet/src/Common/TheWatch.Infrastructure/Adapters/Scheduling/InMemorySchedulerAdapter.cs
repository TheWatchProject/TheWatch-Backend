using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;
using TheWatch.Application.Scheduling;

namespace TheWatch.Infrastructure.Adapters.Scheduling;

public class InMemorySchedulerAdapter : ISchedulerPort
{
    private readonly IJobScheduler _scheduler;
    private readonly ILogger<InMemorySchedulerAdapter> _logger;

    public InMemorySchedulerAdapter(IJobScheduler scheduler, ILogger<InMemorySchedulerAdapter> logger)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task ScheduleRecurringJobAsync(string jobId, string cronExpression, Func<Task> jobAction, CancellationToken ct = default)
    {
        await _scheduler.ScheduleAsync(
            new ScheduledJobDefinition(jobId, JobSchedule.Cron(cronExpression)),
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await jobAction().ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);
        _logger.LogInformation("Scheduled recurring job {JobId} with cron {Cron}", jobId, cronExpression);
    }

    public async Task ScheduleDelayedJobAsync(string jobId, TimeSpan delay, Func<Task> jobAction, CancellationToken ct = default)
    {
        await _scheduler.ScheduleAsync(
            new ScheduledJobDefinition(jobId, JobSchedule.Delayed(delay)),
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await jobAction().ConfigureAwait(false);
                _logger.LogInformation("Executed delayed job {JobId}", jobId);
            },
            ct).ConfigureAwait(false);
    }

    public async Task CancelJobAsync(string jobId, CancellationToken ct = default)
    {
        if (await _scheduler.CancelAsync(jobId, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("Cancelled job {JobId}", jobId);
        }
    }
}
