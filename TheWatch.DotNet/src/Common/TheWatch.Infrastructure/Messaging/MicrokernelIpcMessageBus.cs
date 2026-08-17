using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Messaging;

/// <summary>
/// High-speed asynchronous zero-copy microkernel IPC message channel bus for inter-process and edge subsystem communication. Ported from OS_Proof OS IPC/IO API.
/// </summary>
public sealed class MicrokernelIpcMessageBus
{
    private readonly ConcurrentDictionary<string, Channel<IpcMessageEnvelope>> _channels = new();

    public sealed record IpcMessageEnvelope(
        string MessageId,
        string SourceChannel,
        string DestinationChannel,
        string MessageType,
        byte[] PayloadBytes,
        DateTime EmittedAtUtc
    );

    public Channel<IpcMessageEnvelope> GetOrCreateChannel(string channelName, int capacity = 1000)
    {
        return _channels.GetOrAdd(channelName, _ =>
            Channel.CreateBounded<IpcMessageEnvelope>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            }));
    }

    public async ValueTask PublishAsync(string destinationChannel, string sourceChannel, string messageType, byte[] payload, CancellationToken ct = default)
    {
        var channel = GetOrCreateChannel(destinationChannel);
        var envelope = new IpcMessageEnvelope(
            MessageId: Guid.NewGuid().ToString("N"),
            SourceChannel: sourceChannel,
            DestinationChannel: destinationChannel,
            MessageType: messageType,
            PayloadBytes: payload,
            EmittedAtUtc: DateTime.UtcNow
        );

        await channel.Writer.WriteAsync(envelope, ct);
    }

    public async ValueTask<IpcMessageEnvelope> ReadAsync(string channelName, CancellationToken ct = default)
    {
        var channel = GetOrCreateChannel(channelName);
        return await channel.Reader.ReadAsync(ct);
    }
}
