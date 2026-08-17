// <copyright file="IncidentHub.cs" company="The Watch, LLC">
// Copyright (c) 2026 Barton Milnor Mallory, The Watch, LLC. All rights reserved.
// </copyright>

/// <summary>
/// Source file: src/Services/TheWatch.MobileBff/Hubs/IncidentHub.cs
/// Module: Enterprise Microservices, BFF Gateway & Tactical Dispatch
/// Defines: class IncidentHub
/// Namespace: TheWatch.MobileBff.Hubs
/// </summary>
using Microsoft.AspNetCore.SignalR;
using TheWatch.Contracts;

namespace TheWatch.MobileBff.Hubs;

/// <summary>
/// Real-time SignalR Hub for streaming incident status, updates, and emergency broadcasts.
/// </summary>
public sealed class IncidentHub : Hub<HubContracts.IIncidentClient>
{
    private readonly ILogger<IncidentHub> _logger;

    public IncidentHub(ILogger<IncidentHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinIncidentChannel(string incidentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"incident_{incidentId}");
        _logger.LogInformation("Connection {ConnectionId} subscribed to incident channel {IncidentId}", Context.ConnectionId, incidentId);
    }

    public async Task LeaveIncidentChannel(string incidentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"incident_{incidentId}");
        _logger.LogInformation("Connection {ConnectionId} left incident channel {IncidentId}", Context.ConnectionId, incidentId);
    }

    public async Task BroadcastIncidentUpdate(Guid incidentId, string status, string reason)
    {
        await Clients.Group($"incident_{incidentId}").OnIncidentStatusChanged(incidentId, status, reason);
        _logger.LogInformation("Broadcasted status update {Status} for incident {IncidentId}", status, incidentId);
    }
}
