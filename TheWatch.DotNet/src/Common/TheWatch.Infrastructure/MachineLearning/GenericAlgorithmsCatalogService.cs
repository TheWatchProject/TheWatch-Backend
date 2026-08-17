using System;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;

namespace TheWatch.Infrastructure.MachineLearning;

/// <summary>
/// Generic Algorithms Catalog and Execution Service providing metadata, time/space complexity, and execution dispatch for classic algorithms. Ported from OS_Proof.
/// </summary>
public sealed class GenericAlgorithmsCatalogService
{
    private readonly Dictionary<string, AlgorithmMetadata> _catalog = new();

    public GenericAlgorithmsCatalogService()
    {
        RegisterDefaultAlgorithms();
    }

    private void RegisterDefaultAlgorithms()
    {
        AddAlgorithm(new AlgorithmMetadata(
            AlgorithmId: "ALG-A-STAR",
            Name: "Constrained A* Hazard Avoidance Pathfinder",
            Category: "Graph / Pathfinding",
            TimeComplexity: "O(E + V log V)",
            SpaceComplexity: "O(V)",
            Description: "Shortest path heuristic search avoiding threat polygons and hazardous incident perimeters.",
            IsDeterministic: true
        ));

        AddAlgorithm(new AlgorithmMetadata(
            AlgorithmId: "ALG-QUADTREE",
            Name: "Spatial QuadTree Hierarchical Partitioning",
            Category: "Spatial / Tree",
            TimeComplexity: "O(log N)",
            SpaceComplexity: "O(N)",
            Description: "Two-dimensional space decomposition for high-concurrency proximity and geohash range searches.",
            IsDeterministic: true
        ));

        AddAlgorithm(new AlgorithmMetadata(
            AlgorithmId: "ALG-MERKLE",
            Name: "Balanced Merkle Tree Cryptographic Batch Seal",
            Category: "Cryptography / Tree",
            TimeComplexity: "O(N log N)",
            SpaceComplexity: "O(N)",
            Description: "RFC 6962 compliant binary hash tree for tamper-evident digital custody notarization.",
            IsDeterministic: true
        ));
    }

    public void AddAlgorithm(AlgorithmMetadata meta)
    {
        _catalog[meta.AlgorithmId] = meta;
    }

    public AlgorithmMetadata? GetAlgorithm(string algorithmId)
    {
        return _catalog.TryGetValue(algorithmId, out var meta) ? meta : null;
    }

    public IEnumerable<AlgorithmMetadata> SearchByCategory(string category)
    {
        return _catalog.Values.Where(a => a.Category.Contains(category, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
