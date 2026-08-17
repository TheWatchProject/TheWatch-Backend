// <copyright file="MeshRelayHub.cs" company="The Watch, LLC">
// Copyright (c) 2026 Barton Milnor Mallory, The Watch, LLC. All rights reserved.
// </copyright>

/// <summary>
/// Source file: src/Services/TheWatch.MobileBff/Hubs/MeshRelayHub.cs
/// Module: Enterprise Microservices, BFF Gateway & Tactical Dispatch
/// Defines: class MeshRelayHub
/// Namespace: TheWatch.MobileBff.Hubs
/// </summary>
using Microsoft.AspNetCore.SignalR;
using TheWatch.Contracts;

namespace TheWatch.MobileBff.Hubs;

/// <summary>
/// Real-time SignalR Hub for off-grid LoRa/BLE mesh packet relay and node peer-to-peer synchronization.
/// </summary>
public sealed class MeshRelayHub : Hub<HubContracts.IMeshRelayClient>
{
    private readonly ILogger<MeshRelayHub> _logger;

    public MeshRelayHub(ILogger<MeshRelayHub> logger)
    {
        _logger = logger;
    }

    public async Task RelayPacket(MeshContracts.MeshPacket packet)
    {
        _logger.LogDebug("Relaying mesh packet {PacketId} from {Source} to {Destination} (Hops: {HopCount})",
            packet.PacketId, packet.SourceNodeId, packet.DestinationNodeId, packet.HopCount);

        if (packet.DestinationNodeId.Equals("BROADCAST", StringComparison.OrdinalIgnoreCase))
        {
            await Clients.Others.OnMeshPacketReceived(packet);
        }
        else
        {
            await Clients.User(packet.DestinationNodeId).OnMeshPacketReceived(packet);
        }
    }

    public async Task UpdateNodeHeartbeat(MeshContracts.MeshNodeStatus nodeStatus)
    {
        await Clients.All.OnMeshNodeStatusChanged(nodeStatus);
    }
}
