using System;
using System.Collections.Generic;
using System.Linq;

namespace TheWatch.Geospatial.Db;

/// <summary>
/// High-speed recursive 2D Spatial QuadTree indexing engine for rapid proximity search.
/// </summary>
/// <typeparam name="TData">Payload data type.</typeparam>
public class SpatialQuadTreeIndex<TData>
{
    private const int NodeCapacity = 16;
    private const int MaxDepth = 10;

    private readonly GeoBoundingBox _boundary;
    private readonly int _depth;
    private readonly List<SpatialPoint<TData>> _points = new();
    private SpatialQuadTreeIndex<TData>[]? _children;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new QuadTree node covering the specified boundary.
    /// </summary>
    /// <param name="boundary">Geographical bounding box of node.</param>
    /// <param name="depth">Current tree depth level.</param>
    public SpatialQuadTreeIndex(GeoBoundingBox boundary, int depth = 0)
    {
        _boundary = boundary;
        _depth = depth;
    }

    /// <summary>
    /// Inserts a spatial point into the QuadTree.
    /// </summary>
    /// <param name="point">The spatial point to index.</param>
    /// <returns>True if insertion was successful; otherwise false.</returns>
    public bool Insert(SpatialPoint<TData> point)
    {
        if (!_boundary.Contains(point.Latitude, point.Longitude))
            return false;

        lock (_lock)
        {
            if (_children == null && (_points.Count < NodeCapacity || _depth >= MaxDepth))
            {
                _points.Add(point);
                return true;
            }

            if (_children == null)
            {
                Subdivide();
                // Re-distribute existing points
                foreach (var p in _points)
                {
                    foreach (var child in _children)
                    {
                        if (child.Insert(p)) break;
                    }
                }
                _points.Clear();
            }

            foreach (var child in _children)
            {
                if (child.Insert(point)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Queries all points located within a specific bounding box.
    /// </summary>
    /// <param name="range">Query range boundary.</param>
    /// <returns>List of matching spatial points.</returns>
    public List<SpatialPoint<TData>> QueryRange(GeoBoundingBox range)
    {
        var results = new List<SpatialPoint<TData>>();

        if (!_boundary.Intersects(range))
            return results;

        lock (_lock)
        {
            foreach (var point in _points)
            {
                if (range.Contains(point.Latitude, point.Longitude))
                    results.Add(point);
            }

            if (_children != null)
            {
                foreach (var child in _children)
                {
                    results.AddRange(child.QueryRange(range));
                }
            }
        }

        return results;
    }

    private void Subdivide()
    {
        var midLat = (_boundary.MinLat + _boundary.MaxLat) / 2.0;
        var midLon = (_boundary.MinLon + _boundary.MaxLon) / 2.0;

        _children = new SpatialQuadTreeIndex<TData>[]
        {
            new(new GeoBoundingBox(_boundary.MinLat, midLat, _boundary.MinLon, midLon), _depth + 1), // SW
            new(new GeoBoundingBox(midLat, _boundary.MaxLat, _boundary.MinLon, midLon), _depth + 1), // NW
            new(new GeoBoundingBox(_boundary.MinLat, midLat, midLon, _boundary.MaxLon), _depth + 1), // SE
            new(new GeoBoundingBox(midLat, _boundary.MaxLat, midLon, _boundary.MaxLon), _depth + 1)  // NE
        };
    }
}
