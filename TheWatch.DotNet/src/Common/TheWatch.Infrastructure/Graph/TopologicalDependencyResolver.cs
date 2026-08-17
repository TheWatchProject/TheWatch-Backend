using System;
using System.Collections.Generic;
using System.Linq;

namespace TheWatch.Infrastructure.Graph;

/// <summary>
/// Topological dependency sorting and cycle detection engine for subagent task DAGs, component initialization, and boot graphs. Derived from OS_Proof Rust TheAlgorithms and Neo4j starter.
/// </summary>
public sealed class TopologicalDependencyResolver
{
    private readonly Dictionary<string, HashSet<string>> _dependencies = new();

    public void AddDependency(string node, string dependsOn)
    {
        if (!_dependencies.ContainsKey(node))
        {
            _dependencies[node] = new HashSet<string>();
        }
        if (!_dependencies.ContainsKey(dependsOn))
        {
            _dependencies[dependsOn] = new HashSet<string>();
        }
        _dependencies[dependsOn].Add(node);
    }

    public (List<string> Order, bool HasCycle) ResolveExecutionOrder()
    {
        var inDegree = new Dictionary<string, int>();
        foreach (var node in _dependencies.Keys)
        {
            inDegree[node] = 0;
        }

        foreach (var edges in _dependencies.Values)
        {
            foreach (var dep in edges)
            {
                inDegree[dep]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var order = new List<string>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            order.Add(node);

            if (_dependencies.TryGetValue(node, out var outgoing))
            {
                foreach (var neighbor in outgoing)
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        bool hasCycle = order.Count != _dependencies.Count;
        return (order, hasCycle);
    }
}
