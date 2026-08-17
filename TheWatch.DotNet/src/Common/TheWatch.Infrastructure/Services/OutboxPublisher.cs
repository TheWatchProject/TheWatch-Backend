using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of the Outbox pattern for reliable event publishing.
/// Ensures domain events are published after database transaction commits.
/// </summary>
public class OutboxPublisher : IOutboxPublisher
{
    private readonly IOutboxRepository _outboxRepository;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        IOutboxRepository outboxRepository,
        ILogger<OutboxPublisher> logger)
    {
        _outboxRepository = outboxRepository;
        _logger = logger;
    }

    public async Task PublishEventAsync<TEvent>(TEvent @event, string correlationId, CancellationToken cancellationToken = default) 
        where TEvent : class
    {
        var messageType = typeof(TEvent).Name;
        var payload = JsonSerializer.Serialize(@event);

        var outboxMessage = new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            MessageType = messageType,
            Payload = payload,
            CreatedAt = DateTime.UtcNow,
            Status = "pending",
            RetryCount = 0,
            CorrelationId = correlationId
        };

        await _outboxRepository.CreateAsync(outboxMessage, cancellationToken);

        _logger.LogInformation("Event {MessageType} queued in outbox with ID {OutboxMessageId} and correlation {CorrelationId}",
            messageType, outboxMessage.OutboxMessageId, correlationId);
    }
}
