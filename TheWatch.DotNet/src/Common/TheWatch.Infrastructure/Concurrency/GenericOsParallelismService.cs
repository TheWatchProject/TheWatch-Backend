using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Concurrency;

/// <summary>
/// Generic OS Parallelism and Concurrent Worker Pool service supporting bounded parallelism and batch aggregation. Ported from OS_Proof.
/// </summary>
public sealed class GenericOsParallelismService
{
    public async Task<ParallelExecutionSummary> ExecuteBatchInParallelAsync(
        ParallelExecutionRequest request,
        Func<string, CancellationToken, Task> taskExecutor,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int completed = 0;
        int failed = 0;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(request.Timeout);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = request.MaxDegreeOfParallelism > 0 ? request.MaxDegreeOfParallelism : Environment.ProcessorCount,
            CancellationToken = cts.Token
        };

        await Parallel.ForEachAsync(request.TaskPayloads, parallelOptions, async (payload, token) =>
        {
            try
            {
                await taskExecutor(payload, token);
                Interlocked.Increment(ref completed);
            }
            catch
            {
                Interlocked.Increment(ref failed);
            }
        });

        sw.Stop();
        return new ParallelExecutionSummary(
            BatchId: request.BatchId,
            CompletedCount: completed,
            FailedCount: failed,
            ElapsedTime: sw.Elapsed
        );
    }
}
