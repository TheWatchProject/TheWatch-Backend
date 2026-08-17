using System;
using System.Collections.Generic;

namespace TheWatch.Geospatial.Db;

/// <summary>
/// Represents a geographical point in WGS-84 coordinate space with optional metadata payload.
/// </summary>
/// <typeparam name="TData">Payload type associated with the spatial coordinate.</typeparam>
public record SpatialPoint<TData>(string Id, double Latitude, double Longitude, TData Data, DateTime Timestamp);

/// <summary>
/// Represents a 2D geographical bounding box defined by minimum and maximum coordinates.
/// </summary>
/// <param name="MinLat">Southernmost latitude boundary.</param>
/// <param name="MaxLat">Northernmost latitude boundary.</param>
/// <param name="MinLon">Westernmost longitude boundary.</param>
/// <param name="MaxLon">Easternmost longitude boundary.</param>
public record GeoBoundingBox(double MinLat, double MaxLat, double MinLon, double MaxLon)
{
    /// <summary>
    /// Checks if a latitude/longitude coordinate falls inside the bounding box.
    /// </summary>
    public bool Contains(double lat, double lon) =>
        lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon;

    /// <summary>
    /// Checks if this bounding box intersects another bounding box.
    /// </summary>
    public bool Intersects(GeoBoundingBox other) =>
        !(other.MinLat > MaxLat || other.MaxLat < MinLat || other.MinLon > MaxLon || other.MaxLon < MinLon);
}
