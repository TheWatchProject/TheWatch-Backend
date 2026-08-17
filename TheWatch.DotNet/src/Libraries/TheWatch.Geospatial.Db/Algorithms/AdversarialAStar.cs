using System;
using System.Collections.Generic;
using System.Linq;

namespace TheWatch.Geospatial.Db.Algorithms;

public sealed record GraphNode(string Id, double Latitude, double Longitude, List<string> NeighborIds);
public sealed record ThreatRepulsor(double Latitude, double Longitude, double ThreatWeight, double LethalRadiusMeters);

public interface IAdversarialPathfinder
{
    IReadOnlyList<string> CalculateRepulsivePath(
        Dictionary<string, GraphNode> graph,
        string startNodeId,
        string targetNodeId,
        IReadOnlyList<ThreatRepulsor> threats);
}

/// <summary>
/// Adversarial A* Pathfinder using quadratic threat field repulsion: f(n) = g(n) + h(n) + sum(K / dist(n, threat)^2).
/// </summary>
public sealed class AdversarialAStarPathfinder : IAdversarialPathfinder
{
    private const double RepulsionConstantK = 50_000.0;

    public IReadOnlyList<string> CalculateRepulsivePath(
        Dictionary<string, GraphNode> graph,
        string startNodeId,
        string targetNodeId,
        IReadOnlyList<ThreatRepulsor> threats)
    {
        if (!graph.ContainsKey(startNodeId) || !graph.ContainsKey(targetNodeId))
            return Array.Empty<string>();

        var targetNode = graph[targetNodeId];
        var openSet = new PriorityQueue<string, double>();
        var gScore = new Dictionary<string, double>();
        var cameFrom = new Dictionary<string, string>();

        foreach (var key in graph.Keys)
        {
            gScore[key] = double.PositiveInfinity;
        }

        gScore[startNodeId] = 0;
        openSet.Enqueue(startNodeId, Heuristic(graph[startNodeId], targetNode));

        while (openSet.Count > 0)
        {
            var currentId = openSet.Dequeue();
            if (currentId == targetNodeId)
            {
                return ReconstructPath(cameFrom, currentId);
            }

            var currentNode = graph[currentId];

            foreach (var neighborId in currentNode.NeighborIds)
            {
                if (!graph.TryGetValue(neighborId, out var neighborNode)) continue;

                // Check lethal threat boundary
                if (IsInsideLethalRadius(neighborNode, threats)) continue;

                double stepCost = HaversineMeters(currentNode.Latitude, currentNode.Longitude, neighborNode.Latitude, neighborNode.Longitude);
                double threatRepulsion = ComputeThreatPenalty(neighborNode, threats);
                double tentativeGScore = gScore[currentId] + stepCost + threatRepulsion;

                if (tentativeGScore < gScore[neighborId])
                {
                    cameFrom[neighborId] = currentId;
                    gScore[neighborId] = tentativeGScore;
                    double fScore = tentativeGScore + Heuristic(neighborNode, targetNode);
                    openSet.Enqueue(neighborId, fScore);
                }
            }
        }

        return Array.Empty<string>();
    }

    private static double Heuristic(GraphNode a, GraphNode b) =>
        HaversineMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);

    private static bool IsInsideLethalRadius(GraphNode node, IReadOnlyList<ThreatRepulsor> threats)
    {
        foreach (var t in threats)
        {
            var dist = HaversineMeters(node.Latitude, node.Longitude, t.Latitude, t.Longitude);
            if (dist <= t.LethalRadiusMeters) return true;
        }
        return false;
    }

    private static double ComputeThreatPenalty(GraphNode node, IReadOnlyList<ThreatRepulsor> threats)
    {
        double penalty = 0.0;
        foreach (var t in threats)
        {
            var dist = Math.Max(1.0, HaversineMeters(node.Latitude, node.Longitude, t.Latitude, t.Longitude));
            penalty += (t.ThreatWeight * RepulsionConstantK) / (dist * dist);
        }
        return penalty;
    }

    private static IReadOnlyList<string> ReconstructPath(Dictionary<string, string> cameFrom, string current)
    {
        var path = new List<string> { current };
        while (cameFrom.TryGetValue(current, out var prev))
        {
            current = prev;
            path.Insert(0, current);
        }
        return path;
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 6_371_000.0 * (2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }
}
