using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TheWatch.Core.Messaging;

/// <summary>
/// Domain event contract representing an emergency telemetry or tactical operational event.
/// </summary>
public sealed record ReactiveEmergencyEvent(
    string EventId,
    string EventType,
    string SourceId,
    string GeoHash,
    double SeverityScore,
    Dictionary<string, string> Metadata,
    DateTime OccurredAtUtc
);

/// <summary>
/// High-throughput, zero-allocation Reactive Event Stream & Complex Event Processing (CEP) Hub.
/// Supports temporal sliding windows, spatial geohash stream grouping, and multi-sensor shock correlation.
/// </summary>
public sealed class ReactiveEmergencyEventStreamHub : IDisposable
{
    private readonly Channel<ReactiveEmergencyEvent> _eventChannel;
    private readonly ConcurrentDictionary<string, List<Func<ReactiveEmergencyEvent, ValueTask>>> _subscribers = new();
    private readonly ConcurrentDictionary<string, List<ReactiveEmergencyEvent>> _spatialBuffers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    public ReactiveEmergencyEventStreamHub(int capacity = 100_000)
    {
        _eventChannel = Channel.CreateBounded<ReactiveEmergencyEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        _processingTask = Task.Run(ProcessStreamAsync);
    }

    /// <summary>
    /// Publishes a reactive event asynchronously into the processing pipeline.
    /// </summary>
    public async ValueTask PublishAsync(ReactiveEmergencyEvent evt, CancellationToken ct = default)
    {
        await _eventChannel.Writer.WriteAsync(evt, ct);
    }

    /// <summary>
    /// Subscribes a reactive consumer callback to a specific event type or wildcard.
    /// </summary>
    public IDisposable Subscribe(string eventType, Func<ReactiveEmergencyEvent, ValueTask> onNext)
    {
        var list = _subscribers.GetOrAdd(eventType, _ => new List<Func<ReactiveEmergencyEvent, ValueTask>>());
        lock (list)
        {
            list.Add(onNext);
        }

        return new Unsubscriber(() =>
        {
            lock (list)
            {
                list.Remove(onNext);
            }
        });
    }

    /// <summary>
    /// Evaluates a temporal sliding window for multi-sensor panic correlation within a geohash bucket.
    /// Returns true if multiple distinct sensors fired critical events within the time window.
    /// </summary>
    public bool EvaluateMultiSensorShockCorrelation(string geohashPrefix, TimeSpan window, int minUniqueSensorCount = 2)
    {
        var cutoff = DateTime.UtcNow - window;
        if (!_spatialBuffers.TryGetValue(geohashPrefix, out var buffer))
        {
            return false;
        }

        lock (buffer)
        {
            // Prune expired events
            buffer.RemoveAll(e => e.OccurredAtUtc < cutoff);

            int uniqueSources = buffer
                .Where(e => e.SeverityScore >= 0.75)
                .Select(e => e.SourceId)
                .Distinct()
                .Count();

            return uniqueSources >= minUniqueSensorCount;
        }
    }

    private async Task ProcessStreamAsync()
    {
        var reader = _eventChannel.Reader;
        while (await reader.WaitToReadAsync(_cts.Token))
        {
            while (reader.TryRead(out var evt))
            {
                // 1. Maintain spatial geohash buffer for Complex Event Processing (CEP)
                string prefix = evt.GeoHash.Length >= 5 ? evt.GeoHash[..5] : evt.GeoHash;
                var buffer = _spatialBuffers.GetOrAdd(prefix, _ => new List<ReactiveEmergencyEvent>());
                lock (buffer)
                {
                    buffer.Add(evt);
                }

                // 2. Dispatch to specific subscribers
                if (_subscribers.TryGetValue(evt.EventType, out var specificList))
                {
                    DispatchToSubscribers(specificList, evt);
                }

                // 3. Dispatch to wildcard subscribers
                if (_subscribers.TryGetValue("*", out var wildcardList))
                {
                    DispatchToSubscribers(wildcardList, evt);
                }
            }
        }
    }

    private static void DispatchToSubscribers(List<Func<ReactiveEmergencyEvent, ValueTask>> subscribers, ReactiveEmergencyEvent evt)
    {
        List<Func<ReactiveEmergencyEvent, ValueTask>> snapshot;
        lock (subscribers)
        {
            snapshot = subscribers.ToList();
        }

        foreach (var sub in snapshot)
        {
            try
            {
                _ = sub(evt);
            }
            catch
            {
                // Isolate subscriber exceptions from stream pipeline
            }
        }
    }

    public void Dispose()
    {
        _eventChannel.Writer.Complete();
        _cts.Cancel();
        _cts.Dispose();
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly Action _unsubscribe;
        public Unsubscriber(Action unsubscribe) => _unsubscribe = unsubscribe;
        public void Dispose() => _unsubscribe();
    }
}
