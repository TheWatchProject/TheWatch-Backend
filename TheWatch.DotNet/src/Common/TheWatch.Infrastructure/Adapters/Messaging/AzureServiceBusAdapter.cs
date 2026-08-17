using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;
using TheWatch.Core.Messaging;

namespace TheWatch.Infrastructure.Adapters.Messaging;

public class AzureServiceBusAdapter : IMessageBusPort, IMessageBus
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<AzureServiceBusAdapter> _logger;

    public AzureServiceBusAdapter(ServiceBusClient client, ILogger<AzureServiceBusAdapter> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task PublishEventAsync<T>(string topic, T payload, string? correlationId = null, CancellationToken ct = default)
    {
        var sender = _client.CreateSender(topic);
        var json = JsonSerializer.Serialize(payload);
        var message = new ServiceBusMessage(json)
        {
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString()
        };

        _logger.LogInformation("AzureServiceBusAdapter: Publishing to topic {Topic} with CorrelationId {CorrelationId}", topic, message.CorrelationId);
        await sender.SendMessageAsync(message, ct);
    }

    public async Task SendCommandAsync<T>(string queue, T command, CancellationToken ct = default)
    {
        var sender = _client.CreateSender(queue);
        var json = JsonSerializer.Serialize(command);
        var message = new ServiceBusMessage(json);
        await sender.SendMessageAsync(message, ct);
    }

    public async ValueTask PublishAsync<T>(MessageEnvelope<T> envelope, CancellationToken cancellationToken = default)
    {
        var sender = _client.CreateSender(envelope.Type);
        var message = new ServiceBusMessage(JsonSerializer.Serialize(envelope))
        {
            MessageId = envelope.Id,
            CorrelationId = envelope.CorrelationId,
            ContentType = "application/json",
            Subject = envelope.Type
        };
        if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
            message.ApplicationProperties["IdempotencyKey"] = envelope.IdempotencyKey;
        await sender.SendMessageAsync(message, cancellationToken);
    }
}
