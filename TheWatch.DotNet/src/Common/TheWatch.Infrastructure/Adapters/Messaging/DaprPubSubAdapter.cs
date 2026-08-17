using System;
using System.Threading;
using System.Threading.Tasks;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;
using TheWatch.Core.Messaging;

namespace TheWatch.Infrastructure.Adapters.Messaging;

public class DaprPubSubAdapter : IMessageBusPort, IMessageBus
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprPubSubAdapter> _logger;
    private const string PubSubName = "thewatch-pubsub";

    public DaprPubSubAdapter(DaprClient daprClient, ILogger<DaprPubSubAdapter> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task PublishEventAsync<T>(string topic, T payload, string? correlationId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("DaprPubSubAdapter: Publishing event to topic {Topic}", topic);
        await _daprClient.PublishEventAsync(PubSubName, topic, payload, ct);
    }

    public async Task SendCommandAsync<T>(string queue, T command, CancellationToken ct = default)
    {
        await _daprClient.PublishEventAsync(PubSubName, queue, command, ct);
    }

    public async ValueTask PublishAsync<T>(MessageEnvelope<T> message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DaprPubSubAdapter: Publishing generated envelope {MessageId} to {MessageType}", message.Id, message.Type);
        await _daprClient.PublishEventAsync(PubSubName, message.Type, message, cancellationToken);
    }
}
