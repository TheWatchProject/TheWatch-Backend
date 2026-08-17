using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;
using static TheWatch.Contracts.DecentralizedMeshContracts;

namespace TheWatch.Infrastructure.Adapters.Mesh;

public interface IDecentralizedNodeDiscoveryService
{
    void RegisterLocalNode(DecentralizedNodeHeartbeat localNode);
    void IngestPeerHeartbeat(DecentralizedNodeHeartbeat peerHeartbeat);
    IReadOnlyList<DecentralizedNodeHeartbeat> GetActivePeers();
    string? ElectClusterLeader(string clusterId);
    OfflineAutonomousDispatchRecord ExecuteOfflineDispatch(string incidentId, string unitId, string localNodeId);
}

/// <summary>
/// Autonomous Decentralized P2P Discovery, Cluster Leader Election & Offline Failover Engine.
/// </summary>
public sealed class DecentralizedNodeDiscoveryService : IDecentralizedNodeDiscoveryService
{
    private DecentralizedNodeHeartbeat? _localNode;
    private readonly ConcurrentDictionary<string, DecentralizedNodeHeartbeat> _peers = new();
    private readonly ConcurrentDictionary<string, OfflineAutonomousDispatchRecord> _offlineDispatches = new();

    public void RegisterLocalNode(DecentralizedNodeHeartbeat localNode)
    {
        _localNode = localNode;
        _peers[localNode.NodeId] = localNode;
    }

    public void IngestPeerHeartbeat(DecentralizedNodeHeartbeat peerHeartbeat)
    {
        _peers[peerHeartbeat.NodeId] = peerHeartbeat;
    }

    public IReadOnlyList<DecentralizedNodeHeartbeat> GetActivePeers()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        return _peers.Values.Where(p => p.HeartbeatTimeUtc >= cutoff).ToList();
    }

    public string? ElectClusterLeader(string clusterId)
    {
        var active = GetActivePeers().Where(p => p.ClusterId == clusterId).ToList();
        if (!active.Any()) return null;

        // Leader election priority: Highest Merkle sequence number, then highest NodeRole, then alphabetical NodeId
        var leader = active
            .OrderByDescending(n => n.MerkleBlockSequenceNumber)
            .ThenByDescending(n => (int)n.Role)
            .ThenBy(n => n.NodeId)
            .First();

        return leader.NodeId;
    }

    public OfflineAutonomousDispatchRecord ExecuteOfflineDispatch(string incidentId, string unitId, string localNodeId)
    {
        var record = new OfflineAutonomousDispatchRecord(
            $"OFFLINE-DISP-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            incidentId,
            unitId,
            localNodeId,
            DigitalSignature: $"SIG-ECDSA-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            DateTime.UtcNow
        );

        _offlineDispatches[record.DispatchId] = record;
        return record;
    }
}
