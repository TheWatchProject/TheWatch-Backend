using System;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;

namespace TheWatch.Geospatial.Db;

/// <summary>
/// Computational geometry algorithms including Graham Scan Convex Hull and Ramer-Douglas-Peucker (RDP) polygon simplification. Ported from OS_Proof TheAlgorithms.
/// </summary>
public static class SpatialGeometryAlgorithms
{
    public static List<Point2D> ComputeConvexHull(List<Point2D> points)
    {
        if (points.Count <= 3) return new List<Point2D>(points);

        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

        var lower = new List<Point2D>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && CrossProduct(lower[^2], lower[^1], p) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(p);
        }

        var upper = new List<Point2D>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && CrossProduct(upper[^2], upper[^1], p) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);

        lower.AddRange(upper);
        return lower;
    }

    public static List<Point2D> SimplifyPolylineRdp(List<Point2D> points, double epsilon)
    {
        if (points.Count < 3) return new List<Point2D>(points);

        double maxDist = 0;
        int index = 0;
        for (int i = 1; i < points.Count - 1; i++)
        {
            double dist = PerpendicularDistance(points[i], points[0], points[^1]);
            if (dist > maxDist)
            {
                index = i;
                maxDist = dist;
            }
        }

        if (maxDist > epsilon)
        {
            var left = SimplifyPolylineRdp(points.Take(index + 1).ToList(), epsilon);
            var right = SimplifyPolylineRdp(points.Skip(index).ToList(), epsilon);

            var result = new List<Point2D>(left);
            result.RemoveAt(result.Count - 1);
            result.AddRange(right);
            return result;
        }

        return new List<Point2D> { points[0], points[^1] };
    }

    private static double CrossProduct(Point2D o, Point2D a, Point2D b)
    {
        return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }

    private static double PerpendicularDistance(Point2D p, Point2D lineStart, Point2D lineEnd)
    {
        double dx = lineEnd.X - lineStart.X;
        double dy = lineEnd.Y - lineStart.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            return Math.Sqrt(Math.Pow(p.X - lineStart.X, 2) + Math.Pow(p.Y - lineStart.Y, 2));
        }

        double num = Math.Abs(dy * p.X - dx * p.Y + lineEnd.X * lineStart.Y - lineEnd.Y * lineStart.X);
        double den = Math.Sqrt(dx * dx + dy * dy);
        return num / den;
    }
}
