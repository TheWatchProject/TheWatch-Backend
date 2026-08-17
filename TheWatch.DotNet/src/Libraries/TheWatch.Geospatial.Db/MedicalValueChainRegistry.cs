using System.Collections.Concurrent;
using TheWatch.Contracts;
using static TheWatch.Contracts.MedicalSupplyChainContracts;

namespace TheWatch.Geospatial.Db;

public interface IMedicalValueChainRegistry
{
    void RegisterHospital(HospitalFacility facility);
    void UpdateBedCapacity(string facilityId, HospitalBedCapacity capacity);
    void RegisterMedicalAsset(EmergencyMedicalAsset asset);
    MedicalCasualtyRoutingResult RouteCasualtyToFacility(MedicalCasualtyRoutingRequest request);
    IReadOnlyList<HospitalFacility> GetAllHospitals();
    IReadOnlyList<EmergencyMedicalAsset> GetNearbyAssets(double latitude, double longitude, double radiusKm);
}

public sealed class MedicalValueChainRegistry : IMedicalValueChainRegistry
{
    private readonly ConcurrentDictionary<string, HospitalFacility> _hospitals = new();
    private readonly ConcurrentDictionary<string, EmergencyMedicalAsset> _assets = new();

    public MedicalValueChainRegistry()
    {
        SeedDefaultHospitals();
    }

    private void SeedDefaultHospitals()
    {
        var sfGeneral = new HospitalFacility(
            "HOSP-SFGH-01",
            "Zuckerberg San Francisco General Hospital and Trauma Center",
            "622110",
            TraumaCenterLevel.Level1,
            37.7558,
            -122.4047,
            "1001 Potrero Ave, San Francisco, CA 94110",
            true,
            new HospitalBedCapacity(397, 45, 12, 6, 8, 4, DateTime.UtcNow),
            new List<string> { "Level 1 Trauma", "Burn Center", "Helipad", "Mass Casualty Surge" }
        );

        var ucsfMissionBay = new HospitalFacility(
            "HOSP-UCSF-02",
            "UCSF Medical Center at Mission Bay",
            "622110",
            TraumaCenterLevel.PediatricTraumaLevel1,
            37.7675,
            -122.3920,
            "1825 4th St, San Francisco, CA 94158",
            true,
            new HospitalBedCapacity(289, 32, 10, 4, 18, 0, DateTime.UtcNow),
            new List<string> { "Pediatric Trauma Level 1", "Neonatal ICU", "Helipad" }
        );

        _hospitals.TryAdd(sfGeneral.FacilityId, sfGeneral);
        _hospitals.TryAdd(ucsfMissionBay.FacilityId, ucsfMissionBay);
    }

    public void RegisterHospital(HospitalFacility facility)
    {
        _hospitals[facility.FacilityId] = facility;
    }

    public void UpdateBedCapacity(string facilityId, HospitalBedCapacity capacity)
    {
        if (_hospitals.TryGetValue(facilityId, out var hosp))
        {
            _hospitals[facilityId] = hosp with { BedCapacity = capacity };
        }
    }

    public void RegisterMedicalAsset(EmergencyMedicalAsset asset)
    {
        _assets[asset.AssetId] = asset;
    }

    public MedicalCasualtyRoutingResult RouteCasualtyToFacility(MedicalCasualtyRoutingRequest request)
    {
        var candidates = _hospitals.Values.ToList();

        if (request.RequiresTrauma1)
        {
            candidates = candidates.Where(h => h.TraumaLevel is TraumaCenterLevel.Level1 or TraumaCenterLevel.Level2).ToList();
        }

        if (request.RequiresBurnCare)
        {
            candidates = candidates.Where(h => h.TraumaLevel == TraumaCenterLevel.BurnCenter || h.SpecializedCapabilities.Contains("Burn Center")).ToList();
        }

        if (request.RequiresPediatricCare)
        {
            candidates = candidates.Where(h => h.TraumaLevel == TraumaCenterLevel.PediatricTraumaLevel1 || h.BedCapacity.AvailablePediatricBeds > 0).ToList();
        }

        if (candidates.Count == 0)
        {
            candidates = _hospitals.Values.ToList();
        }

        // Find nearest facility with bed availability
        var best = candidates
            .Select(h =>
            {
                double dLat = (h.Latitude - request.PatientLatitude) * 111.0;
                double dLon = (h.Longitude - request.PatientLongitude) * (111.0 * Math.Cos(request.PatientLatitude * Math.PI / 180.0));
                double distanceKm = Math.Sqrt(dLat * dLat + dLon * dLon);
                return new { Hospital = h, DistanceKm = distanceKm };
            })
            .OrderBy(x => x.DistanceKm)
            .FirstOrDefault();

        var selected = best?.Hospital ?? _hospitals.Values.First();
        double finalDistance = best?.DistanceKm ?? 5.0;
        var estMinutes = Math.Max(2, (int)(finalDistance / 50.0 * 60)); // Avg 50km/h transit speed

        string bedType = request.TriageLevel == 1 ? "Trauma Resuscitation Bay" :
                         request.RequiresBurnCare ? "Burn Unit" :
                         request.RequiresPediatricCare ? "Pediatric ICU" : "Emergency Ward Bed";

        return new MedicalCasualtyRoutingResult(
            request.IncidentId,
            request.CasualtyId,
            selected.FacilityId,
            selected.Name,
            Math.Round(finalDistance, 2),
            TimeSpan.FromMinutes(estMinutes),
            bedType,
            selected.HasHelipad,
            DateTime.UtcNow
        );
    }

    public IReadOnlyList<HospitalFacility> GetAllHospitals() => _hospitals.Values.ToList();

    public IReadOnlyList<EmergencyMedicalAsset> GetNearbyAssets(double latitude, double longitude, double radiusKm)
    {
        return _assets.Values.Where(a =>
        {
            double dLat = (a.CurrentLatitude - latitude) * 111.0;
            double dLon = (a.CurrentLongitude - longitude) * (111.0 * Math.Cos(latitude * Math.PI / 180.0));
            double distanceKm = Math.Sqrt(dLat * dLat + dLon * dLon);
            return distanceKm <= radiusKm;
        }).ToList();
    }
}
