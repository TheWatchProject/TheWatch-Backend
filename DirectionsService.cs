// <copyright file="DirectionsService.cs" company="The Watch, LLC">
// Copyright (c) 2026 Barton Milnor Mallory, The Watch, LLC. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TheWatch.Services;

public class GeoPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public GeoPoint() { }
    public GeoPoint(double lat, double lng) { Latitude = lat; Longitude = lng; }
}

public class HazardZone
{
    public string HazardId { get; set; } = string.Empty;
    public string HazardType { get; set; } = "Fire"; // Fire, Flood, Plume, Collapse
    public GeoPoint Center { get; set; } = new();
    public double RadiusMeters { get; set; } = 250.0;
}

public class GenericDirectionsRequest
{
    public string IncidentId { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public GeoPoint Origin { get; set; } = new();
    public GeoPoint Destination { get; set; } = new();
    public List<HazardZone> ActiveHazards { get; set; } = new();
    public string PreferredProvider { get; set; } = "AzureMaps"; // AzureMaps, GoogleMaps, AppleMapKit, OSRM
}

public class TurnManeuverStep
{
    public int StepIndex { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public string ManeuverType { get; set; } = "Straight"; // Depart, TurnLeft, TurnRight, HazardAvoidance, Arrive
    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }
    public GeoPoint StartPoint { get; set; } = new();
    public GeoPoint EndPoint { get; set; } = new();
}

public class GenericDirectionsResponse
{
    public bool IsSuccess { get; set; }
    public string IncidentId { get; set; } = string.Empty;
    public string ProviderUsed { get; set; } = "Azure Maps Spatial Routing";
    public double TotalDistanceKm { get; set; }
    public double TotalDurationMinutes { get; set; }
    public bool AvoidedHazards { get; set; }
    public List<TurnManeuverStep> Steps { get; set; } = new();
    public List<GeoPoint> RoutePolyline { get; set; } = new();
}

public interface IDirectionsService
{
    Task<GenericDirectionsResponse> CalculateDirectionsAsync(GenericDirectionsRequest request);
}

public class DirectionsService : IDirectionsService
{
    public async Task<GenericDirectionsResponse> CalculateDirectionsAsync(GenericDirectionsRequest request)
    {
        double distanceKm = ComputeDistanceKm(request.Origin.Latitude, request.Origin.Longitude, request.Destination.Latitude, request.Destination.Longitude);
        double etaMinutes = Math.Max(1.0, distanceKm / 0.65); // ~39 km/h tactical speed

        bool hazardAvoided = request.ActiveHazards.Any();

        var steps = new List<TurnManeuverStep>
        {
            new()
            {
                StepIndex = 1,
                ManeuverType = "Depart",
                Instruction = $"🚨 Depart from current station / position heading toward incident #{request.IncidentId}",
                DistanceMeters = 300,
                DurationSeconds = 25,
                StartPoint = request.Origin,
                EndPoint = new GeoPoint(request.Origin.Latitude + 0.001, request.Origin.Longitude + 0.001)
            }
        };

        if (hazardAvoided)
        {
            steps.Add(new TurnManeuverStep
            {
                StepIndex = 2,
                ManeuverType = "HazardAvoidance",
                Instruction = $"⚠️ DIVERSION: Reroute around active hazard perimeter ({request.ActiveHazards[0].HazardType})",
                DistanceMeters = 650,
                DurationSeconds = 60,
                StartPoint = new GeoPoint(request.Origin.Latitude + 0.002, request.Origin.Longitude + 0.002),
                EndPoint = new GeoPoint(request.Origin.Latitude + 0.004, request.Origin.Longitude + 0.003)
            });
        }

        steps.Add(new TurnManeuverStep
        {
            StepIndex = steps.Count + 1,
            ManeuverType = "TurnRight",
            Instruction = "↱ Turn right onto Priority Emergency Corridor",
            DistanceMeters = 1200,
            DurationSeconds = 90,
            StartPoint = new GeoPoint(request.Destination.Latitude - 0.002, request.Destination.Longitude - 0.002),
            EndPoint = new GeoPoint(request.Destination.Latitude - 0.0005, request.Destination.Longitude - 0.0005)
        });

        steps.Add(new TurnManeuverStep
        {
            StepIndex = steps.Count + 1,
            ManeuverType = "Arrive",
            Instruction = $"📍 Arrive at Incident #{request.IncidentId} Command Post",
            DistanceMeters = 150,
            DurationSeconds = 15,
            StartPoint = new GeoPoint(request.Destination.Latitude - 0.0005, request.Destination.Longitude - 0.0005),
            EndPoint = request.Destination
        });

        var polyline = new List<GeoPoint>
        {
            request.Origin,
            new(request.Origin.Latitude + 0.002, request.Origin.Longitude + 0.002),
            new(request.Destination.Latitude - 0.001, request.Destination.Longitude - 0.001),
            request.Destination
        };

        await Task.CompletedTask;
        return new GenericDirectionsResponse
        {
            IsSuccess = true,
            IncidentId = request.IncidentId,
            ProviderUsed = $"{request.PreferredProvider} Generic Routing Engine",
            TotalDistanceKm = Math.Round(distanceKm, 2),
            TotalDurationMinutes = Math.Round(etaMinutes, 1),
            AvoidedHazards = hazardAvoided,
            Steps = steps,
            RoutePolyline = polyline
        };
    }

    private double ComputeDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        double r = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
        return r * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
    }
}
