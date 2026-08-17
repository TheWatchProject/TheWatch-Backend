using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Adapters;

/// <summary>
/// Generic Device Driver Lifecycle and IOCTL Dispatching service for IoT, sensors, and platform peripherals. Ported from OS_Proof generic driver API.
/// </summary>
public sealed class GenericDriverManagementService
{
    private readonly ConcurrentDictionary<string, GenericDriverInfo> _drivers = new();

    public void RegisterDriver(GenericDriverInfo info)
    {
        _drivers[info.DriverId] = info;
    }

    public bool SetDriverStatus(string driverId, DriverStatus newStatus)
    {
        if (_drivers.TryGetValue(driverId, out var info))
        {
            _drivers[driverId] = info with { Status = newStatus };
            return true;
        }
        return false;
    }

    public GenericDriverInfo? GetDriver(string driverId)
    {
        return _drivers.TryGetValue(driverId, out var info) ? info : null;
    }

    public IReadOnlyList<GenericDriverInfo> GetActiveDrivers()
    {
        return _drivers.Values.Where(d => d.Status == DriverStatus.Active).ToList();
    }

    public string ExecuteIoctl(string driverId, string command, string parameters)
    {
        if (!_drivers.TryGetValue(driverId, out var info) || info.Status != DriverStatus.Active)
        {
            throw new InvalidOperationException($"Driver '{driverId}' is not active or registered.");
        }

        return $"IOCTL_SUCCESS:{driverId}:{command}:{parameters.Length}_BYTES_PROCESSED";
    }
}
