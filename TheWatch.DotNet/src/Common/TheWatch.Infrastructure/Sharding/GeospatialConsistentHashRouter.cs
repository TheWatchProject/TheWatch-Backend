using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Sharding;

/// <summary>
/// Consistent hashing ring and geospatial partition router for distributing CAD incident records and GPS streams across database shards.
/// </summary>
public sealed class GeospatialConsistentHashRouter
{
    private readonly SortedDictionary<uint, ShardNode> _ring = new();
    private readonly List<ShardNode> _shards = new();

    public void RegisterShard(ShardNode shard)
    {
        _shards.Add(shard);
        int vNodes = Math.Max(1, shard.VirtualNodeCount);

        for (int i = 0; i < vNodes; i++)
        {
            string vNodeKey = $"{shard.ShardId}#vnode-{i}";
            uint hash = HashKey(vNodeKey);
            _ring[hash] = shard;
        }
    }

    public PartitionRouteResult RouteGeohash(string geohash)
    {
        if (!_ring.Any())
        {
            throw new InvalidOperationException("No database shards registered in consistent hash ring.");
        }

        uint keyHash = HashKey(geohash);

        // Find first node with hash >= keyHash (ring wrap-around)
        var targetEntry = _ring.FirstOrDefault(kvp => kvp.Key >= keyHash);
        var targetShard = targetEntry.Value ?? _ring.First().Value;
        uint targetNodeHash = targetEntry.Value != null ? targetEntry.Key : _ring.First().Key;

        return new PartitionRouteResult(
            ShardId: targetShard.ShardId,
            Geohash: geohash,
            VirtualNodeHash: (int)targetNodeHash,
            TargetConnection: targetShard.ConnectionString
        );
    }

    private static uint HashKey(string key)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToUInt32(bytes, 0);
    }
}
