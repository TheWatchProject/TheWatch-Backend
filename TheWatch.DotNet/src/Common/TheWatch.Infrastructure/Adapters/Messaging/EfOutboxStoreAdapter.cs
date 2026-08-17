using System.Runtime.CompilerServices;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Messaging.Reliability;

namespace TheWatch.Infrastructure.Adapters.Messaging;

/// <summary>Adapts the existing EF-backed domain outbox to the generated messaging contract.</summary>
public sealed class EfOutboxStoreAdapter : IOutboxStore
{
    private readonly IOutboxRepository _repository;

    /// <summary>Creates an EF-backed generated outbox store.</summary>
    public EfOutboxStoreAdapter(IOutboxRepository repository) => _repository = repository;

    /// <inheritdoc />
    public async ValueTask AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
    {
        await _repository.CreateAsync(new Core.Entities.OutboxMessage
        {
            OutboxMessageId = entry.Id,
            MessageType = entry.MessageType,
            Payload = entry.Payload,
            CorrelationId = entry.CorrelationId,
            CreatedAt = entry.CreatedAt.UtcDateTime,
            Status = "pending",
            RetryCount = entry.Attempts,
            ErrorMessage = entry.LastError
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<OutboxEntry> ReadPendingAsync(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = await _repository.GetPendingMessagesAsync(batchSize, cancellationToken);
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new OutboxEntry(
                message.OutboxMessageId,
                message.MessageType,
                message.Payload,
                message.CorrelationId ?? message.OutboxMessageId.ToString("N"),
                new DateTimeOffset(DateTime.SpecifyKind(message.CreatedAt, DateTimeKind.Utc)),
                Status: MapStatus(message.Status),
                Attempts: message.RetryCount,
                LastError: message.ErrorMessage);
        }
    }

    /// <inheritdoc />
    public ValueTask MarkPublishedAsync(Guid id, CancellationToken cancellationToken = default) =>
        new(_repository.MarkAsProcessedAsync(id, cancellationToken));

    /// <inheritdoc />
    public ValueTask MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default) =>
        new(_repository.MarkAsFailedAsync(id, error, cancellationToken));

    private static OutboxEntryStatus MapStatus(string status) => status.ToLowerInvariant() switch
    {
        "processing" => OutboxEntryStatus.Processing,
        "completed" => OutboxEntryStatus.Published,
        "failed" => OutboxEntryStatus.Failed,
        _ => OutboxEntryStatus.Pending,
    };
}
