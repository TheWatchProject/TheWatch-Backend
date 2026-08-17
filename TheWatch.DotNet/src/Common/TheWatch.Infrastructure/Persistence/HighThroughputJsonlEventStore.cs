using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Persistence;

/// <summary>
/// High-throughput, thread-safe, append-only JSONL Event Store with sequence numbering and cryptographic indexing. Ported from OS_Proof JSONL database API.
/// </summary>
public sealed class HighThroughputJsonlEventStore
{
    private readonly ConcurrentBag<JsonlEventRecord> _inMemoryBuffer = new();
    private long _currentSequence = 0;

    public sealed record JsonlEventRecord(
        long SequenceNumber,
        string EventType,
        string AggregateId,
        string PayloadJson,
        string RecordHashSha256,
        DateTime TimestampUtc
    );

    public JsonlEventRecord AppendEvent(string eventType, string aggregateId, object payload)
    {
        long seq = Interlocked.Increment(ref _currentSequence);
        string payloadJson = JsonSerializer.Serialize(payload);
        string hash = ComputeSha256($"{seq}:{eventType}:{aggregateId}:{payloadJson}");

        var record = new JsonlEventRecord(
            SequenceNumber: seq,
            EventType: eventType,
            AggregateId: aggregateId,
            PayloadJson: payloadJson,
            RecordHashSha256: hash,
            TimestampUtc: DateTime.UtcNow
        );

        _inMemoryBuffer.Add(record);
        return record;
    }

    public IReadOnlyList<JsonlEventRecord> QueryEvents(string? eventType = null, string? aggregateId = null)
    {
        return _inMemoryBuffer
            .Where(e => (eventType == null || e.EventType == eventType) &&
                        (aggregateId == null || e.AggregateId == aggregateId))
            .OrderBy(e => e.SequenceNumber)
            .ToList();
    }

    public string ExportToJsonlString()
    {
        var sb = new StringBuilder();
        foreach (var evt in _inMemoryBuffer.OrderBy(e => e.SequenceNumber))
        {
            sb.AppendLine(JsonSerializer.Serialize(evt));
        }
        return sb.ToString();
    }

    private static string ComputeSha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
