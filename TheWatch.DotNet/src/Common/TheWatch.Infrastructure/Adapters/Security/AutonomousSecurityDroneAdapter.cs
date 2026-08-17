using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Security;

public class AutonomousSecurityDroneAdapter : ISecurityDroneAndPatrolPort
{
    private readonly ILogger<AutonomousSecurityDroneAdapter> _logger;

    public AutonomousSecurityDroneAdapter(ILogger<AutonomousSecurityDroneAdapter> logger)
    {
        _logger = logger;
    }

    public async Task<bool> DispatchAutonomousDronePatrolAsync(string droneId, IEnumerable<DroneWaypoint> flightPath, bool enableThermalIr = true, CancellationToken ct = default)
    {
        _logger.LogWarning("🚁 Dispatched Autonomous Security Drone {DroneId} with Thermal IR={Thermal}. Waypoints locked.", droneId, enableThermalIr);
        await Task.CompletedTask;
        return true;
    }

    public Task<DroneTelemetryStatus> GetDroneTelemetryAsync(string droneId, CancellationToken ct = default)
    {
        return Task.FromResult(new DroneTelemetryStatus(
            DroneId: droneId,
            BatteryPct: 88.5,
            CurrentLat: 37.7749,
            CurrentLon: -122.4194,
            FlightState: "AutonomousPerimeterSweep",
            ThermalCameraActive: true
        ));
    }

    public Task<bool> RecordGuardPatrolCheckpointAsync(GuardPatrolCheckpoint checkpoint, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    public Task<bool> TriggerGuardSilentDuressPanicAsync(string guardId, string postLocation, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}
