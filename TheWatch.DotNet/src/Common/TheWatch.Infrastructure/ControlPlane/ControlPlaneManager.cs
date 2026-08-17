using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.ControlPlane;

/// <summary>
/// Centralized Control Plane orchestrator managing dynamic routing policies,
/// active cluster nodes, and global feature flags.
/// </summary>
public class ControlPlaneManager
{
    private readonly ILogger<ControlPlaneManager> _logger;
    private readonly ConcurrentDictionary<string, string> _featureFlags = new();
    private readonly ConcurrentDictionary<string, NodeHealthStatus> _registeredNodes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlPlaneManager"/> class.
    /// </summary>
    /// <param name="logger">Logger service.</param>
    public ControlPlaneManager(ILogger<ControlPlaneManager> logger)
    {
        _logger = logger;
        _featureFlags["EnableAutonomousDroneDispatch"] = "true";
        _featureFlags["EnableOfflineMeshRelay"] = "true";
        _featureFlags["EnablePostQuantumAuth"] = "false";
    }

    /// <summary>
    /// Registers a node heartbeat ping with the control plane.
    /// </summary>
    /// <param name="nodeId">Unique cluster node ID.</param>
    /// <param name="serviceName">Name of the service (e.g., IncidentService).</param>
    /// <param name="status">Current health status.</param>
    public void RegisterNodeHeartbeat(string nodeId, string serviceName, string status)
    {
        _registeredNodes[nodeId] = new NodeHealthStatus(nodeId, serviceName, status, DateTime.UtcNow);
        _logger.LogDebug("Control Plane: Heartbeat registered for Node {NodeId} ({Service}) - {Status}", nodeId, serviceName, status);
    }

    /// <summary>
    /// Evaluates if a dynamic platform feature flag is active.
    /// </summary>
    /// <param name="flagName">The feature flag key.</param>
    /// <returns>True if enabled; otherwise false.</returns>
    public bool IsFeatureEnabled(string flagName)
    {
        return _featureFlags.TryGetValue(flagName, out var val) && bool.TryParse(val, out var enabled) && enabled;
    }

    /// <summary>
    /// Returns the live inventory of all registered cluster nodes.
    /// </summary>
    public IReadOnlyDictionary<string, NodeHealthStatus> GetActiveNodeInventory() => _registeredNodes;
}

/// <summary>
/// Record holding cluster node health state.
/// </summary>
/// <param name="NodeId">Unique node identifier.</param>
/// <param name="ServiceName">Service name.</param>
/// <param name="Status">Health state.</param>
/// <param name="LastHeartbeat">Timestamp of last heartbeat.</param>
public record NodeHealthStatus(string NodeId, string ServiceName, string Status, DateTime LastHeartbeat);
