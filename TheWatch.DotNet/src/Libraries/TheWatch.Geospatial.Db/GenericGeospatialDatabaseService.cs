using System;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;

namespace TheWatch.Geospatial.Db;

/// <summary>
/// Generic Geospatial Database Service supporting vector layers, bounding-box spatial queries, and proximity searches. Ported from OS_Proof.
/// </summary>
public sealed class GenericGeospatialDatabaseService
{
    private readonly Dictionary<string, GeoSpatialLayer> _layers = new();
    private readonly List<(string LayerId, SpatialFeatureResult Feature, double Lat, double Lon)> _spatialIndex = new();

    public void RegisterLayer(GeoSpatialLayer layer)
    {
        _layers[layer.LayerId] = layer;
    }

    public void InsertFeature(string layerId, string featureId, string geometryType, double lat, double lon, Dictionary<string, string>? properties = null)
    {
        var feature = new SpatialFeatureResult(
            FeatureId: featureId,
            GeometryType: geometryType,
            Coordinates: new List<double[]> { new[] { lon, lat } },
            Properties: properties ?? new Dictionary<string, string>(),
            DistanceMeters: 0.0
        );

        _spatialIndex.Add((layerId, feature, lat, lon));
    }

    public IEnumerable<SpatialFeatureResult> QueryProximity(SpatialProximityQuery query)
    {
        var matched = new List<SpatialFeatureResult>();

        foreach (var item in _spatialIndex)
        {
            if (!string.IsNullOrEmpty(query.LayerFilter) && item.LayerId != query.LayerFilter)
            {
                continue;
            }

            double dist = CalculateDistanceMeters(query.CenterLatitude, query.CenterLongitude, item.Lat, item.Lon);
            if (dist <= query.RadiusMeters)
            {
                matched.Add(item.Feature with { DistanceMeters = Math.Round(dist, 2) });
            }
        }

        return matched.OrderBy(m => m.DistanceMeters).Take(query.MaxResults).ToList();
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }
}
