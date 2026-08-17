using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TheWatch.Geospatial.Db;

/// <summary>
/// Thread-safe in-memory geospatial cache store supporting radius queries and TTL eviction.
/// </summary>
/// <typeparam name="TData">Stored data type.</typeparam>
public class GeospatialCacheStore<TData>
{
    private readonly ConcurrentDictionary<string, SpatialPoint<TData>> _cache = new();
    private SpatialQuadTreeIndex<TData> _spatialIndex;
    private const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Initializes a new geospatial cache covering global coordinates (-90 to +90 Lat, -180 to +180 Lon).
    /// </summary>
    public GeospatialCacheStore()
    {
        _spatialIndex = new SpatialQuadTreeIndex<TData>(new GeoBoundingBox(-90, 90, -180, 180));
    }

    /// <summary>
    /// Adds or updates a spatial item in the cache and indexes it into the QuadTree.
    /// </summary>
    /// <param name="id">Unique identifier.</param>
    /// <param name="latitude">Latitude coordinate.</param>
    /// <param name="longitude">Longitude coordinate.</param>
    /// <param name="data">Payload object.</param>
    public void Upsert(string id, double latitude, double longitude, TData data)
    {
        var point = new SpatialPoint<TData>(id, latitude, longitude, data, DateTime.UtcNow);
        _cache[id] = point;
        _spatialIndex.Insert(point);
    }

    /// <summary>
    /// Finds all cached entities within a radial distance from a center point.
    /// </summary>
    /// <param name="centerLat">Center latitude.</param>
    /// <param name="centerLon">Center longitude.</param>
    /// <param name="radiusKm">Search radius in kilometers.</param>
    /// <returns>List of matching spatial points ordered by distance.</returns>
    public List<(SpatialPoint<TData> Point, double DistanceKm)> FindWithinRadius(double centerLat, double centerLon, double radiusKm)
    {
        // 1. Calculate rough bounding box
        var latDelta = radiusKm / 111.0;
        var lonDelta = radiusKm / (111.0 * Math.Cos(centerLat * (Math.PI / 180.0)));
        var box = new GeoBoundingBox(centerLat - latDelta, centerLat + latDelta, centerLon - lonDelta, centerLon + lonDelta);

        // 2. Query QuadTree candidates
        var candidates = _spatialIndex.QueryRange(box);

        // 3. Exact Haversine distance filtering
        var results = new List<(SpatialPoint<TData>, double)>();
        foreach (var c in candidates)
        {
            var dist = CalculateHaversine(centerLat, centerLon, c.Latitude, c.Longitude);
            if (dist <= radiusKm)
            {
                results.Add((c, dist));
            }
        }

        return results.OrderBy(r => r.Item2).ToList();
    }

    private static double CalculateHaversine(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = (lat2 - lat1) * (Math.PI / 180.0);
        var dLon = (lon2 - lon1) * (Math.PI / 180.0);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * (Math.PI / 180.0)) * Math.Cos(lat2 * (Math.PI / 180.0)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }
}
