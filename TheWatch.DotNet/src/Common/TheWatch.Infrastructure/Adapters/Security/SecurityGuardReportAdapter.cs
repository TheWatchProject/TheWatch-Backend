using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Security;

public class SecurityGuardReportAdapter
{
    private readonly ConcurrentBag<GuardPatrolCheckpoint> _checkpointLogs = new();
    private readonly ILogger<SecurityGuardReportAdapter> _logger;

    public SecurityGuardReportAdapter(ILogger<SecurityGuardReportAdapter> logger)
    {
        _logger = logger;
    }

    public Task<bool> RecordCheckpointAsync(GuardPatrolCheckpoint checkpoint, CancellationToken ct = default)
    {
        _checkpointLogs.Add(checkpoint);
        _logger.LogInformation("Guard {GuardId} scanned checkpoint {CheckpointId} at NFC tag {NfcTag} (Lat: {Lat}, Lon: {Lon})",
            checkpoint.GuardId, checkpoint.CheckpointId, checkpoint.NfcTagId, checkpoint.Latitude, checkpoint.Longitude);
        return Task.FromResult(true);
    }

    public Task<bool> RaiseGuardDuressPanicAsync(string guardId, string guardPost, CancellationToken ct = default)
    {
        _logger.LogCritical("🚨 SILENT DURESS PANIC ALARM: Security Officer {GuardId} at Guard Post {Post} activated emergency duress.",
            guardId, guardPost);
        return Task.FromResult(true);
    }
}
