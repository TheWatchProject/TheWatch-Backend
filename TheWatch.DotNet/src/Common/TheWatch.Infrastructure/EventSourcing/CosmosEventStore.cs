using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.EventSourcing;

public class CosmosEventStore : IEventStorePort
{
    private readonly ConcurrentDictionary<string, List<EventEnvelope>> _eventStreams = new();
    private readonly ILogger<CosmosEventStore> _logger;

    public CosmosEventStore(ILogger<CosmosEventStore> logger)
    {
        _logger = logger;
    }

    public async Task AppendEventAsync(string streamId, string eventType, object eventPayload, CancellationToken ct = default)
    {
        var stream = _eventStreams.GetOrAdd(streamId, _ => new List<EventEnvelope>());
        var json = JsonSerializer.Serialize(eventPayload);
        lock (stream)
        {
            var seq = stream.Count + 1;
            var envelope = new EventEnvelope(streamId, eventType, json, seq, DateTimeOffset.UtcNow);
            stream.Add(envelope);
            _logger.LogInformation("Appended event {EventType} to stream {StreamId} at sequence {Sequence}", eventType, streamId, seq);
        }
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<EventEnvelope>> ReadStreamAsync(string streamId, long fromSequence = 0, CancellationToken ct = default)
    {
        if (!_eventStreams.TryGetValue(streamId, out var stream))
        {
            return Array.Empty<EventEnvelope>();
        }

        lock (stream)
        {
            var events = stream.Where(e => e.SequenceNumber >= fromSequence).ToList();
            return Task.FromResult<IReadOnlyList<EventEnvelope>>(events).Result;
        }
    }
}
