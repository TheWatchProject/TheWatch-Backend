using System;
using System.Collections.Generic;
using System.Linq;

namespace TheWatch.Geospatial.Db.Algorithms;

public record SpatialPoint<T>(double Latitude, double Longitude, T Data);

public class KdTreeSpatialIndex<T>
{
    private class Node
    {
        public SpatialPoint<T> Point { get; set; } = default!;
        public Node? Left { get; set; }
        public Node? Right { get; set; }
    }

    private Node? _root;

    public void Build(IEnumerable<SpatialPoint<T>> points)
    {
        var list = points.ToList();
        _root = BuildRecursive(list, depth: 0);
    }

    private Node? BuildRecursive(List<SpatialPoint<T>> points, int depth)
    {
        if (points.Count == 0) return null;

        int axis = depth % 2; // 0 = Latitude, 1 = Longitude
        points.Sort((a, b) => axis == 0 ? a.Latitude.CompareTo(b.Latitude) : a.Longitude.CompareTo(b.Longitude));

        int median = points.Count / 2;
        var node = new Node { Point = points[median] };

        node.Left = BuildRecursive(points.GetRange(0, median), depth + 1);
        node.Right = BuildRecursive(points.GetRange(median + 1, points.Count - (median + 1)), depth + 1);

        return node;
    }

    public SpatialPoint<T>? FindNearest(double targetLat, double targetLon)
    {
        if (_root == null) return null;
        SpatialPoint<T>? best = null;
        double bestDistSq = double.MaxValue;
        SearchNearest(_root, targetLat, targetLon, depth: 0, ref best, ref bestDistSq);
        return best;
    }

    private void SearchNearest(Node? node, double lat, double lon, int depth, ref SpatialPoint<T>? best, ref double bestDistSq)
    {
        if (node == null) return;

        double dLat = node.Point.Latitude - lat;
        double dLon = node.Point.Longitude - lon;
        double distSq = dLat * dLat + dLon * dLon;

        if (distSq < bestDistSq)
        {
            bestDistSq = distSq;
            best = node.Point;
        }

        int axis = depth % 2;
        double axisDelta = axis == 0 ? lat - node.Point.Latitude : lon - node.Point.Longitude;

        Node? first = axisDelta < 0 ? node.Left : node.Right;
        Node? second = axisDelta < 0 ? node.Right : node.Left;

        SearchNearest(first, lat, lon, depth + 1, ref best, ref bestDistSq);

        if (axisDelta * axisDelta < bestDistSq)
        {
            SearchNearest(second, lat, lon, depth + 1, ref best, ref bestDistSq);
        }
    }
}
