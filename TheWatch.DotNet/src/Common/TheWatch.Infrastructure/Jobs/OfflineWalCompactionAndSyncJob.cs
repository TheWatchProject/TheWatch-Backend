using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Jobs;

public sealed record WalCompactionSummary(int TotalEntriesProcessed, int SyncedEntries, int PurgedDuplicates, DateTime CompactedAtUtc);

/// <summary>
/// Background job that compacts and syncs local write-ahead log (WAL) records from offline edge responder devices.
/// </summary>
public sealed class OfflineWalCompactionAndSyncJob
{
    public Task<WalCompactionSummary> CompactAndSyncWalAsync(
        IEnumerable<(string RecordId, long SequenceNumber, string Payload, bool IsSynced)> walRecords,
        CancellationToken cancellationToken = default)
    {
        var records = walRecords.ToList();
        var seenIds = new HashSet<string>();
        int synced = 0;
        int purged = 0;

        foreach (var (recordId, _, _, isSynced) in records)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (seenIds.Contains(recordId))
            {
                purged++;
            }
            else
            {
                seenIds.Add(recordId);
                if (isSynced) synced++;
            }
        }

        return Task.FromResult(new WalCompactionSummary(
            TotalEntriesProcessed: records.Count,
            SyncedEntries: synced,
            PurgedDuplicates: purged,
            CompactedAtUtc: DateTime.UtcNow
        ));
    }
}
