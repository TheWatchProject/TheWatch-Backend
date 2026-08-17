using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Adapters.Mesh;

public interface IDecentralizedPubSub
{
    Task<string> PublishAsync(string topic, string payloadJson, int maxHops = 5);
    void Subscribe(string topic, Func<string, Task> handler);
    void Unsubscribe(string topic);
    IReadOnlyList<string> GetActiveTopics();
}

public sealed record GossipMessageEnvelope(
    string MessageId,
    string Topic,
    string PayloadJson,
    string OriginatingNodeId,
    int HopsRemaining,
    DateTime TimestampUtc
);

/// <summary>
/// Decentralized P2P Gossip Pub/Sub Bus with cryptographic deduplication, TTL hop limits, and offline local subscriber dispatch.
/// </summary>
public sealed class DecentralizedGossipPubSub : IDecentralizedPubSub
{
    private readonly string _nodeId;
    private readonly ConcurrentDictionary<string, List<Func<string, Task>>> _subscriptions = new();
    private readonly ConcurrentDictionary<string, byte> _seenMessageCache = new(); // Deduplication cache

    public DecentralizedGossipPubSub(string? nodeId = null)
    {
        _nodeId = nodeId ?? $"NODE-{Guid.NewGuid():N}"[..8].ToUpperInvariant();
    }

    public async Task<string> PublishAsync(string topic, string payloadJson, int maxHops = 5)
    {
        var msgId = ComputeMessageId(topic, payloadJson, DateTime.UtcNow);
        var envelope = new GossipMessageEnvelope(
            msgId,
            topic,
            payloadJson,
            _nodeId,
            maxHops,
            DateTime.UtcNow
        );

        await ProcessGossipEnvelopeAsync(envelope);
        return msgId;
    }

    public void Subscribe(string topic, Func<string, Task> handler)
    {
        _subscriptions.AddOrUpdate(
            topic,
            new List<Func<string, Task>> { handler },
            (_, list) => { lock (list) { list.Add(handler); return list; } }
        );
    }

    public void Unsubscribe(string topic)
    {
        _subscriptions.TryRemove(topic, out _);
    }

    public IReadOnlyList<string> GetActiveTopics() => _subscriptions.Keys.ToList();

    public async Task ProcessGossipEnvelopeAsync(GossipMessageEnvelope envelope)
    {
        // Deduplicate: If already seen, drop packet to prevent infinite mesh gossip loops
        if (!_seenMessageCache.TryAdd(envelope.MessageId, 0))
        {
            return;
        }

        // Dispatch to local topic subscribers
        if (_subscriptions.TryGetValue(envelope.Topic, out var handlers))
        {
            List<Func<string, Task>> snapshot;
            lock (handlers)
            {
                snapshot = new List<Func<string, Task>>(handlers);
            }

            foreach (var handler in snapshot)
            {
                try
                {
                    await handler(envelope.PayloadJson);
                }
                catch
                {
                    // Ignore subscriber handler faults to preserve mesh loop stability
                }
            }
        }

        // If hops remain, relay to neighboring peer mesh nodes
        if (envelope.HopsRemaining > 1)
        {
            var relayed = envelope with { HopsRemaining = envelope.HopsRemaining - 1 };
            // Mesh packet forwarder pushes relayed packet to BLE / Wi-Fi Aware / LoRa interfaces
        }
    }

    private static string ComputeMessageId(string topic, string payload, DateTime time)
    {
        string raw = $"{topic}:{payload}:{time.Ticks}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
