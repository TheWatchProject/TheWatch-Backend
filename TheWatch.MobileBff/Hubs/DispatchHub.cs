// <copyright file="DispatchHub.cs" company="The Watch, LLC">
// Copyright (c) 2026 Barton Milnor Mallory, The Watch, LLC. All rights reserved.
// </copyright>

/// <summary>
/// Source file: src/Services/TheWatch.MobileBff/Hubs/DispatchHub.cs
/// Module: Enterprise Microservices, BFF Gateway & Tactical Dispatch
/// Defines: class DispatchHub
/// Namespace: TheWatch.MobileBff.Hubs
/// </summary>
using Microsoft.AspNetCore.SignalR;
using TheWatch.Contracts;

namespace TheWatch.MobileBff.Hubs;

/// <summary>
/// Real-time SignalR Hub for CAD unit dispatch assignments and first responder status transitions.
/// </summary>
public sealed class DispatchHub : Hub<HubContracts.IDispatchClient>
{
    private readonly ILogger<DispatchHub> _logger;

    public DispatchHub(ILogger<DispatchHub> logger)
    {
        _logger = logger;
    }

    public async Task DispatchUnit(IncidentContracts.DispatchUnitRequest request)
    {
        _logger.LogInformation("Dispatching unit {UnitType} (Responder: {ResponderId}) to incident {IncidentId}",
            request.UnitType, request.ResponderId, request.IncidentId);
        await Clients.User(request.ResponderId).OnUnitDispatched(request);
    }

    public async Task UpdateUnitStatus(IncidentContracts.ResponderStatusUpdate status)
    {
        _logger.LogInformation("Unit {ResponderId} updated status to {Status}", status.ResponderId, status.StatusCode);
        await Clients.All.OnUnitStatusChanged(status);
    }
}
