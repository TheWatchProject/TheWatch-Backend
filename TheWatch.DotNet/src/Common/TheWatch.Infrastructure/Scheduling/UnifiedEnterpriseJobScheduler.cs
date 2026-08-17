using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheWatch.Contracts;
using TheWatch.Infrastructure.Alerting;
using TheWatch.Infrastructure.Backups;
using TheWatch.Infrastructure.Jobs;
using TheWatch.Infrastructure.Patching;
using TheWatch.Infrastructure.Sharding;

namespace TheWatch.Infrastructure.Scheduling;

/// <summary>
/// Execution definition for a scheduled enterprise job.
/// </summary>
public sealed record ScheduledJobRegistration(
    string JobId,
    string JobName,
    TimeSpan Interval,
    Func<CancellationToken, Task<object>> ExecutionHandler,
    bool IsCritical = false
);

/// <summary>
/// Status report for an executed enterprise job.
/// </summary>
public sealed record JobExecutionReport(
    string JobId,
    string JobName,
    bool Succeeded,
    DateTime ExecutedAtUtc,
    TimeSpan Duration,
    string? ErrorMessage = null
);

/// <summary>
/// Unified Enterprise Job Scheduler that orchestrates, coordinates, and executes all platform background jobs, Delphi consensus tasks, and automated operations.
/// </summary>
public sealed class UnifiedEnterpriseJobScheduler : BackgroundService
{
    private readonly ILogger<UnifiedEnterpriseJobScheduler> _logger;
    private readonly Dictionary<string, ScheduledJobRegistration> _registry = new();
    private readonly List<JobExecutionReport> _history = new();
    private readonly object _lock = new();

    public UnifiedEnterpriseJobScheduler(ILogger<UnifiedEnterpriseJobScheduler> logger)
    {
        _logger = logger;
        RegisterBuiltInJobs();
    }

    public void RegisterJob(ScheduledJobRegistration registration)
    {
        lock (_lock)
        {
            _registry[registration.JobId] = registration;
        }
    }

    public IReadOnlyList<ScheduledJobRegistration> GetRegisteredJobs()
    {
        lock (_lock)
        {
            return new List<ScheduledJobRegistration>(_registry.Values);
        }
    }

    public IReadOnlyList<JobExecutionReport> GetExecutionHistory()
    {
        lock (_lock)
        {
            return new List<JobExecutionReport>(_history);
        }
    }

    public async Task<JobExecutionReport> TriggerJobImmediatelyAsync(string jobId, CancellationToken ct = default)
    {
        ScheduledJobRegistration? job;
        lock (_lock)
        {
            _registry.TryGetValue(jobId, out job);
        }

        if (job == null)
        {
            throw new KeyNotFoundException($"Job with ID '{jobId}' is not registered in the Enterprise Scheduler.");
        }

        var start = DateTime.UtcNow;
        try
        {
            _logger.LogInformation("Executing scheduled job {JobId} ({JobName})", job.JobId, job.JobName);
            await job.ExecutionHandler(ct);
            var report = new JobExecutionReport(job.JobId, job.JobName, true, start, DateTime.UtcNow - start);
            lock (_lock)
            {
                _history.Add(report);
            }
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled job {JobId} failed during execution", job.JobId);
            var report = new JobExecutionReport(job.JobId, job.JobName, false, start, DateTime.UtcNow - start, ex.Message);
            lock (_lock)
            {
                _history.Add(report);
            }
            return report;
        }
    }

    private void RegisterBuiltInJobs()
    {
        // 1. Drone Fleet RTH Battery Watchdog Job
        RegisterJob(new ScheduledJobRegistration(
            JobId: "JOB-DRONE-RTH",
            JobName: "Drone Patrol & RTH Battery Watchdog",
            Interval: TimeSpan.FromSeconds(30),
            ExecutionHandler: async ct =>
            {
                var job = new DronePatrolAndBatteryWatchdogJob();
                var fleet = new List<DroneFleetStatus>
                {
                    new("DRONE-ALPHA-01", 18.0, "PATROLLING", 120.0, false),
                    new("DRONE-BETA-02", 85.0, "PATROLLING", 150.0, false)
                };
                return await job.ExecuteAsync(fleet, ct);
            },
            IsCritical: true
        ));

        // 2. Geofence & Evacuation Breach Evaluator Job
        RegisterJob(new ScheduledJobRegistration(
            JobId: "JOB-GEOFENCE-BREACH",
            JobName: "Dynamic Geofence & Evacuation Breach Evaluator",
            Interval: TimeSpan.FromSeconds(15),
            ExecutionHandler: async ct =>
            {
                var job = new GeofenceAndEvacuationBreachEvaluatorJob();
                var positions = new List<(string TargetId, double Lat, double Lon)>
                {
                    ("CIVILIAN-01", 37.7749, -122.4194),
                    ("RESPONDER-05", 37.7800, -122.4100)
                };
                return await job.EvaluateBreachesAsync(positions, 37.7750, -122.4190, 500.0, "GEO-FIRE-ZONE-01", ct);
            },
            IsCritical: true
        ));

        // 3. Merkle Batch Seal & Cryptographic Notarization Job
        RegisterJob(new ScheduledJobRegistration(
            JobId: "JOB-MERKLE-SEAL",
            JobName: "Merkle Batch Seal & Cryptographic Notarization",
            Interval: TimeSpan.FromMinutes(10),
            ExecutionHandler: async ct =>
            {
                var job = new MerkleBatchSealAndNotarizationJob();
                var leafHashes = new List<string> { "a1b2c3d4", "e5f6g7h8", "i9j0k1l2" };
                return await job.SealBatchAsync(leafHashes, ct);
            }
        ));

        // 4. Mesh Routing Table Pruning Job
        RegisterJob(new ScheduledJobRegistration(
            JobId: "JOB-MESH-PRUNE",
            JobName: "P2P Mesh Routing Table & Vector Clock Pruning",
            Interval: TimeSpan.FromMinutes(1),
            ExecutionHandler: async ct =>
            {
                var job = new MeshRoutingTablePruningJob();
                var nodes = new List<MeshPeerNode>
                {
                    new("NODE-01", DateTime.UtcNow.AddMinutes(-2), "HEALTHY", 1),
                    new("NODE-02", DateTime.UtcNow.AddMinutes(-45), "STALE", 4)
                };
                return await job.PruneRoutingTableAsync(nodes, TimeSpan.FromMinutes(15), ct);
            }
        ));

        // 5. NAICS Supply Chain Risk Reindexing Job
        RegisterJob(new ScheduledJobRegistration(
            JobId: "JOB-NAICS-REINDEX",
            JobName: "NAICS / NAPCS Value Chain Hazard Risk Reindexing",
            Interval: TimeSpan.FromHours(1),
            ExecutionHandler: async ct =>
            {
                var job = new NaicsSupplyChainRiskReindexingJob();
                var industries = new List<(string Code, string Title, double BaselineCrit, double ThreatFactor)>
                {
                    ("221122", "Electric Power Distribution", 0.95, 0.85),
                    ("484110", "General Freight Trucking", 0.70, 0.50)
                };
                return await job.ReindexIndustryRisksAsync(industries, ct);
            }
        ));

        // 6. Offline WAL Compaction & Sync Job
        RegisterJob(new ScheduledJobRegistration(
            JobId: "JOB-WAL-COMPACT",
            JobName: "Offline SQLite WAL Checkpoint & Compaction",
            Interval: TimeSpan.FromHours(6),
            ExecutionHandler: async ct =>
            {
                var job = new OfflineWalCompactionAndSyncJob();
                var records = new List<(string RecordId, long SequenceNumber, string Payload, bool IsSynced)>
                {
                    ("REC-01", 1, "PAYLOAD-1", true),
                    ("REC-02", 2, "PAYLOAD-2", false),
                    ("REC-01", 3, "PAYLOAD-1-UPDATED", true)
                };
                return await job.CompactAndSyncWalAsync(records, ct);
            }
        ));

        // 7. Automated WAL and Database Backup Job
        RegisterJob(new ScheduledJobRegistration(
            JobId: "JOB-BACKUP-WAL",
            JobName: "Automated Database & WAL Snapshot Backup",
            Interval: TimeSpan.FromHours(12),
            ExecutionHandler: async ct =>
            {
                var job = new AutomatedWalAndSnapshotBackupJob();
                byte[] data = Encoding.UTF8.GetBytes("DATABASE_SNAPSHOT_BINARY_PAYLOAD");
                return await job.ExecuteBackupAsync("TheWatch_Production_Cluster", BackupType.FullSnapshot, data, "vault://backups/cluster-01", ct);
            }
        ));

        // 8. Chaos Resilience & Canary Verification Job
        RegisterJob(new ScheduledJobRegistration(
            JobId: "JOB-CHAOS-CANARY",
            JobName: "Chaos Resilience & Synthetic Canary Verification",
            Interval: TimeSpan.FromMinutes(5),
            ExecutionHandler: async ct =>
            {
                var job = new ChaosResilienceAndHeartbeatVerificationJob();
                var nodes = new List<(string NodeId, int LatencyMs, bool Responding)>
                {
                    ("US-EAST-SHARD-01", 25, true),
                    ("US-WEST-SHARD-02", 450, true),
                    ("EU-CENTRAL-SHARD-03", 999, false)
                };
                return await job.VerifyResilienceAsync(nodes, 250, ct);
            }
        ));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Unified Enterprise Job Scheduler started with {Count} registered jobs.", _registry.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = GetRegisteredJobs();
                foreach (var job in jobs)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await TriggerJobImmediatelyAsync(job.JobId, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred in scheduler loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
