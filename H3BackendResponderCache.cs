// <copyright file="H3BackendResponderCache.cs" company="The Watch, LLC">
// Copyright (c) 2026 Barton Milnor Mallory, The Watch, LLC. All rights reserved.
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TheWatch.Services;

public class BackendResponderDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "Police";
    public string Status { get; set; } = "Available";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string H3Index { get; set; } = string.Empty;
    public string Geohash { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public int EtaMinutes { get; set; }
    public string VehicleCallsign { get; set; } = string.Empty;
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public interface IH3BackendResponderCache
{
    string LatLngToH3(double latitude, double longitude, int resolution = 8);
    List<string> GetKRingHexagons(string originH3Index, int kRadius = 1);
    Task<List<BackendResponderDto>> QueryNearbyRespondersAsync(double originLat, double originLng, int kRingRadius = 2, int resolution = 8);
    Task UpdateResponderPositionAsync(BackendResponderDto responder);
    Task SeedDefaultFleetAsync();
}

public class H3BackendResponderCache : IH3BackendResponderCache
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, BackendResponderDto>> _h3IndexMap = new();
    private readonly ConcurrentDictionary<string, string> _responderToH3Map = new();

    public H3BackendResponderCache()
    {
        _ = SeedDefaultFleetAsync();
    }

    public string LatLngToH3(double latitude, double longitude, int resolution = 8)
    {
        resolution = Math.Clamp(resolution, 1, 15);
        double scale = Math.Pow(3, resolution / 2.0) * 100.0;
        long i = (long)Math.Round((longitude + 180.0) * scale);
        long j = (long)Math.Round((latitude + 90.0) * scale * 1.1547);
        ulong h3Raw = 0x8000000000000000UL | ((ulong)resolution << 52) | (((ulong)i & 0xFFFFFFF) << 24) | ((ulong)j & 0xFFFFFFF);
        return $"8{resolution:x}{h3Raw:x13}"[..15];
    }

    public List<string> GetKRingHexagons(string originH3Index, int kRadius = 1)
    {
        var cells = new HashSet<string> { originH3Index };
        if (kRadius <= 0) return cells.ToList();

        long raw = Convert.ToInt64(originH3Index[..Math.Min(15, originH3Index.Length)], 16);
        for (int r = 1; r <= kRadius; r++)
        {
            for (int dir = 0; dir < 6; dir++)
            {
                long neighbor = raw + (r * (dir + 1) * 0x1000);
                cells.Add(neighbor.ToString("x15"));
            }
        }
        return cells.ToList();
    }

    public async Task<List<BackendResponderDto>> QueryNearbyRespondersAsync(double originLat, double originLng, int kRingRadius = 2, int resolution = 8)
    {
        string originH3 = LatLngToH3(originLat, originLng, resolution);
        var queriedCells = GetKRingHexagons(originH3, kRingRadius);
        var found = new List<BackendResponderDto>();

        foreach (var cell in queriedCells)
        {
            if (_h3IndexMap.TryGetValue(cell, out var cellBucket))
            {
                foreach (var responder in cellBucket.Values)
                {
                    double dist = ComputeHaversineDistanceKm(originLat, originLng, responder.Latitude, responder.Longitude);
                    responder.DistanceKm = Math.Round(dist, 2);
                    responder.EtaMinutes = Math.Max(1, (int)Math.Round(dist / 0.6));
                    found.Add(responder);
                }
            }
        }

        if (found.Count == 0)
        {
            foreach (var bucket in _h3IndexMap.Values)
            {
                foreach (var resp in bucket.Values)
                {
                    double dist = ComputeHaversineDistanceKm(originLat, originLng, resp.Latitude, resp.Longitude);
                    resp.DistanceKm = Math.Round(dist, 2);
                    resp.EtaMinutes = Math.Max(1, (int)Math.Round(dist / 0.6));
                    found.Add(resp);
                }
            }
        }

        await Task.CompletedTask;
        return found.OrderBy(r => r.DistanceKm).ToList();
    }

    public async Task UpdateResponderPositionAsync(BackendResponderDto responder)
    {
        if (responder == null) return;

        string newH3 = LatLngToH3(responder.Latitude, responder.Longitude, 8);
        responder.H3Index = newH3;
        responder.LastUpdatedUtc = DateTimeOffset.UtcNow;

        if (_responderToH3Map.TryGetValue(responder.Id, out var oldH3) && oldH3 != newH3)
        {
            if (_h3IndexMap.TryGetValue(oldH3, out var oldBucket))
            {
                oldBucket.TryRemove(responder.Id, out _);
            }
        }

        var bucket = _h3IndexMap.GetOrAdd(newH3, _ => new ConcurrentDictionary<string, BackendResponderDto>());
        bucket[responder.Id] = responder;
        _responderToH3Map[responder.Id] = newH3;

        await Task.CompletedTask;
    }

    public async Task SeedDefaultFleetAsync()
    {
        double centerLat = 37.7749;
        double centerLng = -122.4194;

        var fleet = new List<BackendResponderDto>
        {
            new() { Id = "SFPD-104", Name = "Officer Miller (Unit 104)", Role = "Police", Status = "Available", VehicleCallsign = "SFPD-CRUISER-104", Latitude = centerLat + 0.0035, Longitude = centerLng - 0.0028 },
            new() { Id = "SFFD-ENG-4", Name = "Captain Vance (Engine 4)", Role = "Fire", Status = "EnRoute", VehicleCallsign = "FIRE-ENG-4", Latitude = centerLat - 0.0042, Longitude = centerLng + 0.0031 },
            new() { Id = "SFFD-MEDIC-12", Name = "Paramedic Sarah (Medic 12)", Role = "Paramedic", Status = "Available", VehicleCallsign = "ALS-MEDIC-12", Latitude = centerLat + 0.0061, Longitude = centerLng + 0.0054 },
            new() { Id = "TAC-DRONE-2", Name = "Tactical Recon Drone 2", Role = "Drone", Status = "OnScene", VehicleCallsign = "AERO-DRONE-02", Latitude = centerLat + 0.0012, Longitude = centerLng + 0.0015 },
            new() { Id = "CERT-VOL-8", Name = "CERT Volunteer Dave", Role = "CERT", Status = "Available", VehicleCallsign = "CERT-MOB-88", Latitude = centerLat - 0.0075, Longitude = centerLng - 0.0062 }
        };

        foreach (var unit in fleet)
        {
            await UpdateResponderPositionAsync(unit);
        }
    }

    private double ComputeHaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
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
