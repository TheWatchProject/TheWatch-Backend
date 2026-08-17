using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Concurrency;

/// <summary>
/// Generic OS Message Queue Service supporting Point-to-Point, Pub/Sub, Priority queues, and Dead-Letter Queues (DLQ). Ported from OS_Proof.
/// </summary>
public sealed class GenericOsMessageQueueService
{
    private readonly ConcurrentDictionary<string, Channel<OsQueueMessage>> _p2pQueues = new();
    private readonly ConcurrentDictionary<string, PriorityQueue<OsQueueMessage, int>> _priorityQueues = new();
    private readonly ConcurrentDictionary<string, List<Func<OsQueueMessage, Task>>> _pubSubSubscriptions = new();
    private readonly ConcurrentBag<OsQueueMessage> _deadLetterQueue = new();
    private readonly object _priorityLock = new();

    public async ValueTask EnqueueP2PAsync(string queueName, OsQueueMessage message, CancellationToken ct = default)
    {
        var channel = _p2pQueues.GetOrAdd(queueName, _ => Channel.CreateUnbounded<OsQueueMessage>());
        await channel.Writer.WriteAsync(message, ct);
    }

    public async ValueTask<OsQueueMessage> DequeueP2PAsync(string queueName, CancellationToken ct = default)
    {
        var channel = _p2pQueues.GetOrAdd(queueName, _ => Channel.CreateUnbounded<OsQueueMessage>());
        return await channel.Reader.ReadAsync(ct);
    }

    public void EnqueuePriority(string queueName, OsQueueMessage message)
    {
        lock (_priorityLock)
        {
            var pq = _priorityQueues.GetOrAdd(queueName, _ => new PriorityQueue<OsQueueMessage, int>());
            // Lower number = higher priority in C# PriorityQueue, so invert priority for max-heap behavior
            pq.Enqueue(message, -message.Priority);
        }
    }

    public OsQueueMessage? DequeuePriority(string queueName)
    {
        lock (_priorityLock)
        {
            if (_priorityQueues.TryGetValue(queueName, out var pq) && pq.Count > 0)
            {
                return pq.Dequeue();
            }
            return null;
        }
    }

    public void Subscribe(string topic, Func<OsQueueMessage, Task> subscriber)
    {
        var subs = _pubSubSubscriptions.GetOrAdd(topic, _ => new List<Func<OsQueueMessage, Task>>());
        lock (subs)
        {
            subs.Add(subscriber);
        }
    }

    public async Task PublishAsync(string topic, OsQueueMessage message)
    {
        if (_pubSubSubscriptions.TryGetValue(topic, out var subs))
        {
            List<Func<OsQueueMessage, Task>> snapshot;
            lock (subs)
            {
                snapshot = new List<Func<OsQueueMessage, Task>>(subs);
            }

            foreach (var sub in snapshot)
            {
                try
                {
                    await sub(message);
                }
                catch
                {
                    _deadLetterQueue.Add(message with { DeliveryAttempts = message.DeliveryAttempts + 1 });
                }
            }
        }
    }

    public IReadOnlyList<OsQueueMessage> GetDeadLetterMessages() => _deadLetterQueue.ToArray();
}
