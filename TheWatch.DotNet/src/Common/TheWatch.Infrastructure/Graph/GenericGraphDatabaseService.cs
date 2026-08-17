using System;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.Graph;

/// <summary>
/// Generic Graph Database Service supporting Dijkstra shortest-path traversal, adjacency indexing, and topological node querying. Ported from OS_Proof.
/// </summary>
public sealed class GenericGraphDatabaseService
{
    private readonly Dictionary<string, GraphNode> _nodes = new();
    private readonly Dictionary<string, List<GraphEdge>> _adjacency = new();

    public void AddNode(GraphNode node)
    {
        _nodes[node.NodeId] = node;
        if (!_adjacency.ContainsKey(node.NodeId))
        {
            _adjacency[node.NodeId] = new List<GraphEdge>();
        }
    }

    public void AddEdge(GraphEdge edge)
    {
        if (!_adjacency.ContainsKey(edge.SourceNodeId))
        {
            _adjacency[edge.SourceNodeId] = new List<GraphEdge>();
        }
        _adjacency[edge.SourceNodeId].Add(edge);
    }

    public GraphPathResult FindShortestPath(string startNodeId, string targetNodeId)
    {
        if (!_nodes.ContainsKey(startNodeId) || !_nodes.ContainsKey(targetNodeId))
        {
            return new GraphPathResult(new List<string>(), 0.0, false);
        }

        var distances = new Dictionary<string, double>();
        var previous = new Dictionary<string, string?>();
        var unvisited = new HashSet<string>(_nodes.Keys);

        foreach (var node in _nodes.Keys)
        {
            distances[node] = double.PositiveInfinity;
            previous[node] = null;
        }
        distances[startNodeId] = 0.0;

        while (unvisited.Count > 0)
        {
            var current = unvisited.OrderBy(n => distances[n]).First();
            if (double.IsPositiveInfinity(distances[current])) break;
            if (current == targetNodeId) break;

            unvisited.Remove(current);

            if (_adjacency.TryGetValue(current, out var edges))
            {
                foreach (var edge in edges.Where(e => unvisited.Contains(e.TargetNodeId)))
                {
                    double alt = distances[current] + edge.Weight;
                    if (alt < distances[edge.TargetNodeId])
                    {
                        distances[edge.TargetNodeId] = alt;
                        previous[edge.TargetNodeId] = current;
                    }
                }
            }
        }

        if (double.IsPositiveInfinity(distances[targetNodeId]))
        {
            return new GraphPathResult(new List<string>(), 0.0, false);
        }

        var path = new List<string>();
        string? curr = targetNodeId;
        while (curr != null)
        {
            path.Insert(0, curr);
            curr = previous[curr];
        }

        return new GraphPathResult(path, distances[targetNodeId], true);
    }
}
