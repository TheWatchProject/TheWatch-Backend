namespace TheWatch.Core.Messaging;

/// <summary>
/// Generalized publish/subscribe interface for distributed and edge messaging.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<T>(string topic, T eventData, string? correlationId = null, CancellationToken cancellationToken = default);
}

public interface IEventHandler<in T>
{
    Task HandleAsync(T eventData, string? correlationId = null, CancellationToken cancellationToken = default);
}
