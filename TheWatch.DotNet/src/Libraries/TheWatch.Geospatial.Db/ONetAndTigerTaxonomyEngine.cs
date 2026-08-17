using System.Collections.Concurrent;
using TheWatch.Contracts;
using static TheWatch.Contracts.ONetOccupationalContracts;
using static TheWatch.Contracts.TigerLineGeospatialContracts;

namespace TheWatch.Geospatial.Db;

public interface IONetAndTigerTaxonomyEngine
{
    void RegisterOccupation(OnetOccupation occupation);
    void RegisterCensusBoundary(CensusFipsBoundary boundary);
    void RegisterRoadSegment(TigerRoadSegment segment);
    IReadOnlyList<OnetOccupation> GetOccupationsForNaics(string naicsCode);
    OnetOccupation? GetOccupationBySoc(string socCode);
    TigerGeocodeResult ReverseGeocodeCoordinate(TigerGeocodeRequest request);
}

public sealed class ONetAndTigerTaxonomyEngine : IONetAndTigerTaxonomyEngine
{
    private readonly ConcurrentDictionary<string, OnetOccupation> _occupations = new();
    private readonly ConcurrentDictionary<string, CensusFipsBoundary> _boundaries = new();
    private readonly ConcurrentBag<TigerRoadSegment> _roadSegments = new();

    public ONetAndTigerTaxonomyEngine()
    {
        SeedStandardData();
    }

    private void SeedStandardData()
    {
        // Seed O*NET Occupations
        var onet = new List<OnetOccupation>
        {
            new("29-2042.00", "Emergency Medical Technicians", "Assess injuries, administer emergency medical care, and extricate trapped individuals.", "Healthcare Practitioners and Technical", "621910",
                new List<string> { "Administer first-aid treatment or life support care", "Drive mobile medical equipment", "Communicate with dispatchers" },
                new List<string> { "Medicine and Dentistry", "Customer and Personal Service", "Public Safety and Security" },
                new List<string> { "NREMT-Basic", "CPR/BLS", "EVOC" }),

            new("29-2043.00", "Paramedics", "Administer advanced life support (ALS), perform tracheal intubation, and administer controlled medications.", "Healthcare Practitioners and Technical", "621910",
                new List<string> { "Administer intravenous medications and ECG analysis", "Perform advanced airway management", "Triage mass casualties" },
                new List<string> { "Advanced Cardiology", "Pharmacology", "Emergency Medical Services Systems" },
                new List<string> { "NREMT-Paramedic", "ACLS", "PALS" }),

            new("33-2011.00", "Firefighters", "Control and extinguish fires, protect life and property, and conduct search and rescue.", "Protective Service", "922160",
                new List<string> { "Suppress structural and wildland fires", "Operate hydraulic extrication tools", "Deploy aerial ladders" },
                new List<string> { "Building and Construction", "Public Safety and Security", "Mechanical" },
                new List<string> { "Firefighter I/II (NFPA 1001)", "Hazmat Operations (NFPA 472)" }),

            new("33-3051.00", "Police and Sheriff's Patrol Officers", "Maintain order, enforce laws, protect life and property, and direct traffic.", "Protective Service", "922120",
                new List<string> { "Patrol assigned sectors", "Respond to emergency calls", "Investigate traffic collisions and crimes" },
                new List<string> { "Law and Government", "Public Safety and Security", "Psychology" },
                new List<string> { "POST Certification", "First Responder First Aid" }),

            new("53-2012.00", "Commercial Pilots (UAV / Drone Operators)", "Pilot unmanned aerial vehicles for emergency AED transport and aerial surveillance.", "Transportation and Material Moving", "488190",
                new List<string> { "Execute autonomous waypoint flight plans", "Monitor telemetry link quality", "Operate thermal imaging payloads" },
                new List<string> { "Aeronautics", "Telecommunications", "Computers and Electronics" },
                new List<string> { "FAA Part 107 Remote Pilot Certificate" })
        };

        foreach (var o in onet)
        {
            _occupations.TryAdd(o.SocCode, o);
        }

        // Seed TIGER/Line Census Boundaries (San Francisco Example)
        var sfDowntownTract = new CensusFipsBoundary(
            "06",
            "075",
            "017802",
            "1",
            "060750178021",
            "Census Tract 178.02, Block Group 1, San Francisco County, CA",
            37.7749,
            -122.4194,
            37.7700,
            37.7800,
            -122.4250,
            -122.4100,
            4820
        );

        _boundaries.TryAdd(sfDowntownTract.FullGeoId, sfDowntownTract);

        // Seed TIGER/Line Road Segments
        var marketSt = new TigerRoadSegment(
            1048576,
            "Market St",
            "",
            "Market",
            "St",
            100,
            198,
            101,
            199,
            "94102",
            "94103",
            37.7740,
            -122.4200,
            37.7760,
            -122.4180,
            "S1200"
        );

        var missionSt = new TigerRoadSegment(
            1048577,
            "Mission St",
            "",
            "Mission",
            "St",
            500,
            598,
            501,
            599,
            "94103",
            "94103",
            37.7810,
            -122.4050,
            37.7830,
            -122.4030,
            "S1200"
        );

        _roadSegments.Add(marketSt);
        _roadSegments.Add(missionSt);
    }

    public void RegisterOccupation(OnetOccupation occupation)
    {
        _occupations[occupation.SocCode] = occupation;
    }

    public void RegisterCensusBoundary(CensusFipsBoundary boundary)
    {
        _boundaries[boundary.FullGeoId] = boundary;
    }

    public void RegisterRoadSegment(TigerRoadSegment segment)
    {
        _roadSegments.Add(segment);
    }

    public IReadOnlyList<OnetOccupation> GetOccupationsForNaics(string naicsCode)
    {
        return _occupations.Values.Where(o => o.CorrespondingNaicsCode == naicsCode).ToList();
    }

    public OnetOccupation? GetOccupationBySoc(string socCode)
    {
        return _occupations.TryGetValue(socCode, out var occ) ? occ : null;
    }

    public TigerGeocodeResult ReverseGeocodeCoordinate(TigerGeocodeRequest request)
    {
        // Find matching Census boundary
        CensusFipsBoundary? matchedBoundary = null;
        if (request.IncludeCensusHierarchy)
        {
            matchedBoundary = _boundaries.Values.FirstOrDefault(b =>
                request.Latitude >= b.BoundingBoxMinLat && request.Latitude <= b.BoundingBoxMaxLat &&
                request.Longitude >= b.BoundingBoxMinLon && request.Longitude <= b.BoundingBoxMaxLon);
        }

        // Find nearest TIGER road segment
        TigerRoadSegment? nearestRoad = null;
        double nearestDistanceMeters = double.MaxValue;

        if (request.IncludeNearestRoadSegment)
        {
            foreach (var road in _roadSegments)
            {
                double midLat = (road.StartLatitude + road.EndLatitude) / 2.0;
                double midLon = (road.StartLongitude + road.EndLongitude) / 2.0;

                double dLat = (midLat - request.Latitude) * 111000.0;
                double dLon = (midLon - request.Longitude) * (111000.0 * Math.Cos(request.Latitude * Math.PI / 180.0));
                double dist = Math.Sqrt(dLat * dLat + dLon * dLon);

                if (dist < nearestDistanceMeters)
                {
                    nearestDistanceMeters = dist;
                    nearestRoad = road;
                }
            }
        }

        string formattedAddress = nearestRoad != null
            ? $"{nearestRoad.LeftFromAddress} {nearestRoad.FullStreetName}, {matchedBoundary?.Name ?? "San Francisco, CA"}"
            : $"{request.Latitude:F5}, {request.Longitude:F5}";

        return new TigerGeocodeResult(
            request.Latitude,
            request.Longitude,
            matchedBoundary,
            nearestRoad,
            Math.Round(nearestDistanceMeters, 1),
            formattedAddress,
            DateTime.UtcNow
        );
    }
}
