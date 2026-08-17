using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TheWatch.Core.Messaging;

/// <summary>
/// High-performance thread-safe in-memory event bus with topic routing and channel decoupling.
/// </summary>
public sealed class InMemoryEventBus : IEventPublisher
{
    private readonly ILogger<InMemoryEventBus> _logger;
    private readonly ConcurrentDictionary<string, List<Func<object, string?, CancellationToken, Task>>> _subscribers = new();

    public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
    {
        _logger = logger;
    }

    public void Subscribe<T>(string topic, Func<T, string?, CancellationToken, Task> handler)
    {
        _subscribers.AddOrUpdate(
            topic,
            _ => new List<Func<object, string?, CancellationToken, Task>> { (evt, corrId, ct) => handler((T)evt, corrId, ct) },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add((evt, corrId, ct) => handler((T)evt, corrId, ct));
                }
                return list;
            }
        );
    }

    public async Task PublishAsync<T>(string topic, T eventData, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        if (eventData == null) return;

        _logger.LogDebug("Publishing event of type {EventType} to topic {Topic}", typeof(T).Name, topic);

        if (_subscribers.TryGetValue(topic, out var handlers))
        {
            List<Func<object, string?, CancellationToken, Task>> snapshot;
            lock (handlers)
            {
                snapshot = new List<Func<object, string?, CancellationToken, Task>>(handlers);
            }

            foreach (var handler in snapshot)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    await handler(eventData, correlationId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling event on topic {Topic}", topic);
                }
            }
        }
    }
}
