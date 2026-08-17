using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Jobs;

public sealed record DroneFleetStatus(string DroneId, double BatteryPercentage, string OperationalState, double AltitudeMeters, bool ReturnToHomeTriggered);

/// <summary>
/// Background job that monitors autonomous security drone telemetry, checks battery depletion,
/// and enforces automated Return-To-Home (RTH) safety triggers.
/// </summary>
public sealed class DronePatrolAndBatteryWatchdogJob
{
    public Task<List<DroneFleetStatus>> ExecuteAsync(IEnumerable<DroneFleetStatus> fleet, CancellationToken cancellationToken = default)
    {
        var evaluated = new List<DroneFleetStatus>();

        foreach (var drone in fleet)
        {
            if (cancellationToken.IsCancellationRequested) break;

            bool shouldRth = drone.BatteryPercentage < 20.0 || drone.OperationalState == "HARDWARE_FAULT";
            string newState = shouldRth ? "RETURN_TO_HOME_ACTIVE" : drone.OperationalState;

            evaluated.Add(drone with
            {
                OperationalState = newState,
                ReturnToHomeTriggered = shouldRth
            });
        }

        return Task.FromResult(evaluated);
    }
}
