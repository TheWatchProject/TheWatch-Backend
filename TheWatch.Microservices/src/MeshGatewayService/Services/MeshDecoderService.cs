using System.Collections.Concurrent;
using TheWatch.Contracts;
using TheWatch.Security;

namespace TheWatch.Microservices.Mesh.MeshGatewayService.Services;

public interface IMeshDecoderService
{
    Task<MeshContracts.MeshRelayReport> IngestPacketAsync(MeshContracts.MeshPacket packet);
    Task<IEnumerable<MeshContracts.MeshNodeStatus>> GetActiveNodesAsync();
    Task UpdateNodeStatusAsync(MeshContracts.MeshNodeStatus status);
}

public sealed class MeshDecoderService : IMeshDecoderService
{
    private readonly ILogger<MeshDecoderService> _logger;
    private readonly IFipsCryptoProvider _crypto;
    private readonly ConcurrentDictionary<string, MeshContracts.MeshNodeStatus> _nodes = new();
    private int _packetsReceived;
    private int _packetsForwarded;
    private int _packetsDropped;

    public MeshDecoderService(ILogger<MeshDecoderService> logger, IFipsCryptoProvider crypto)
    {
        _logger = logger;
        _crypto = crypto;
    }

    public Task<MeshContracts.MeshRelayReport> IngestPacketAsync(MeshContracts.MeshPacket packet)
    {
        Interlocked.Increment(ref _packetsReceived);

        try
        {
            if (packet.HopCount > packet.MaxHops)
            {
                _logger.LogWarning("Dropping mesh packet {PacketId}: Hop count {Hops} exceeded limit {MaxHops}",
                    packet.PacketId, packet.HopCount, packet.MaxHops);
                Interlocked.Increment(ref _packetsDropped);
            }
            else
            {
                // Attempt AEAD decrypt if encrypted payload exists
                if (packet.EncryptedPayload.Length > 0 && packet.InitializationVector.Length > 0 && packet.AuthenticationTag.Length > 0)
                {
                    var plaintext = _crypto.Decrypt(packet.EncryptedPayload, packet.InitializationVector, packet.AuthenticationTag);
                    _logger.LogInformation("Successfully decrypted FIPS envelope for packet {PacketId} ({Bytes} bytes)",
                        packet.PacketId, plaintext.Length);
                }

                Interlocked.Increment(ref _packetsForwarded);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt or forward mesh packet {PacketId}", packet.PacketId);
            Interlocked.Increment(ref _packetsDropped);
        }

        var report = new MeshContracts.MeshRelayReport(
            GatewayNodeId: "GATEWAY-CLOUD-01",
            PacketsReceived: _packetsReceived,
            PacketsForwardedToCloud: _packetsForwarded,
            PacketsDropped: _packetsDropped,
            ReportedAtUtc: DateTimeOffset.UtcNow
        );

        return Task.FromResult(report);
    }

    public Task<IEnumerable<MeshContracts.MeshNodeStatus>> GetActiveNodesAsync()
    {
        return Task.FromResult<IEnumerable<MeshContracts.MeshNodeStatus>>(_nodes.Values);
    }

    public Task UpdateNodeStatusAsync(MeshContracts.MeshNodeStatus status)
    {
        _nodes[status.NodeId] = status;
        return Task.CompletedTask;
    }
}
