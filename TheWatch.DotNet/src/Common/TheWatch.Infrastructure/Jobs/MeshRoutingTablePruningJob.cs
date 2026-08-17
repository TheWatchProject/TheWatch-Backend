using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Jobs;

public sealed record MeshPeerNode(string NodeId, DateTime LastHeartbeatUtc, string Status, int HopCount);

/// <summary>
/// Background job that prunes stale or unreachable peer nodes from the P2P Gossip and BLE/LoRa mesh routing tables.
/// </summary>
public sealed class MeshRoutingTablePruningJob
{
    public Task<(List<MeshPeerNode> ActiveNodes, List<string> PrunedNodeIds)> PruneRoutingTableAsync(
        IEnumerable<MeshPeerNode> nodes,
        TimeSpan staleThreshold,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var active = new List<MeshPeerNode>();
        var pruned = new List<string>();

        foreach (var node in nodes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (now - node.LastHeartbeatUtc > staleThreshold)
            {
                pruned.Add(node.NodeId);
            }
            else
            {
                active.Add(node);
            }
        }

        return Task.FromResult((active, pruned));
    }
}
