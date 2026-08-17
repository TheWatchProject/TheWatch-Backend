using System.Collections.Concurrent;
using TheWatch.Contracts;
using static TheWatch.Contracts.InsuranceAndClaimsContracts;
using static TheWatch.Contracts.WeatherAndEnvironmentalContracts;

namespace TheWatch.Geospatial.Db;

public interface IWeatherAndInsuranceEngine
{
    void RecordWeatherObservation(MetarWeatherObservation observation);
    void RegisterSevereAlert(SevereWeatherAlert alert);
    void RegisterPolicy(InsurancePolicyProfile policy);
    void RegisterFemaDeclaration(FemaDisasterDeclaration declaration);
    DroneFlightWeatherSuitability EvaluateDroneFlightSafety(string droneId, double latitude, double longitude, double maxWindGustKmh = 55.0);
    ParametricInsuranceTrigger EvaluateParametricClaim(string policyNumber, string metricType, double observedValue, double thresholdValue, decimal payoutAmount);
    DisasterClaimSubmission CreateAutomatedDisasterClaim(string policyNumber, string incidentId, string damageCategory, decimal estimatedLoss, string merkleEvidenceHash, double lat, double lon);
    IReadOnlyList<InsurancePolicyProfile> GetPoliciesInHazardZone(double centerLat, double centerLon, double radiusKm);
}

public sealed class WeatherAndInsuranceEngine : IWeatherAndInsuranceEngine
{
    private readonly ConcurrentDictionary<string, MetarWeatherObservation> _observations = new();
    private readonly ConcurrentDictionary<string, SevereWeatherAlert> _alerts = new();
    private readonly ConcurrentDictionary<string, InsurancePolicyProfile> _policies = new();
    private readonly ConcurrentDictionary<int, FemaDisasterDeclaration> _femaDeclarations = new();
    private readonly ConcurrentDictionary<string, DisasterClaimSubmission> _claims = new();

    public WeatherAndInsuranceEngine()
    {
        SeedStandardData();
    }

    private void SeedStandardData()
    {
        // Seed default METAR observation
        var sfoObservation = new MetarWeatherObservation(
            "KSFO",
            37.6188,
            -122.3750,
            16.5,
            10.2,
            68.0,
            1013.25,
            24.0, // 24 km/h sustained wind
            38.0, // 38 km/h wind gust
            280,  // Wind from West-Northwest (280 deg)
            16.0, // 16 km visibility (> 10 miles)
            4500, // 4500 ft ceiling
            AviationFlightCategory.VFR,
            DateTime.UtcNow
        );

        _observations.TryAdd(sfoObservation.StationId, sfoObservation);

        // Seed default FEMA Declaration
        var fema4750 = new FemaDisasterDeclaration(
            4750,
            "06",
            "075",
            "California Severe Storms and Flooding",
            "Flood",
            true,
            true,
            DateTime.UtcNow.AddDays(-5)
        );

        _femaDeclarations.TryAdd(fema4750.DisasterNumber, fema4750);

        // Seed default Insurance Policy (NAICS 524126)
        var samplePolicy = new InsurancePolicyProfile(
            "POL-CA-99281",
            "SUBJ-SF-01",
            "Travelers Casualty & Surety (NAIC 19038)",
            "19038",
            PolicyCoverageType.CommercialHazard,
            2500000.00m,
            10000.00m,
            37.7749,
            -122.4194,
            "Zone X", // Minimal flood hazard
            DateTime.UtcNow.AddMonths(11)
        );

        _policies.TryAdd(samplePolicy.PolicyNumber, samplePolicy);
    }

    public void RecordWeatherObservation(MetarWeatherObservation observation)
    {
        _observations[observation.StationId] = observation;
    }

    public void RegisterSevereAlert(SevereWeatherAlert alert)
    {
        _alerts[alert.AlertId] = alert;
    }

    public void RegisterPolicy(InsurancePolicyProfile policy)
    {
        _policies[policy.PolicyNumber] = policy;
    }

    public void RegisterFemaDeclaration(FemaDisasterDeclaration declaration)
    {
        _femaDeclarations[declaration.DisasterNumber] = declaration;
    }

    public DroneFlightWeatherSuitability EvaluateDroneFlightSafety(string droneId, double latitude, double longitude, double maxWindGustKmh = 55.0)
    {
        // Find nearest observation
        var nearest = _observations.Values
            .OrderBy(o => Math.Pow(o.Latitude - latitude, 2) + Math.Pow(o.Longitude - longitude, 2))
            .FirstOrDefault() ?? _observations.Values.First();

        var hazards = new List<string>();
        bool isGo = true;

        if (nearest.WindGustKmh > maxWindGustKmh)
        {
            isGo = false;
            hazards.Add($"Wind gusts exceed airframe structural limit ({nearest.WindGustKmh} km/h > {maxWindGustKmh} km/h).");
        }

        if (nearest.VisibilityKm < 3.0)
        {
            isGo = false;
            hazards.Add($"Optical camera visibility below flight minimums ({nearest.VisibilityKm} km < 3.0 km).");
        }

        if (nearest.TemperatureCelsius <= 0.0 && nearest.RelativeHumidityPercent > 80.0)
        {
            hazards.Add("Moderate airframe icing risk detected.");
        }

        string safetyStatus = isGo ? "Go" : hazards.Count > 1 ? "NoGo" : "Caution";

        return new DroneFlightWeatherSuitability(
            droneId,
            isGo,
            safetyStatus,
            Math.Max(0, maxWindGustKmh - nearest.WindGustKmh),
            Math.Max(0, nearest.VisibilityKm - 3.0),
            nearest.TemperatureCelsius <= 0.0 && nearest.RelativeHumidityPercent > 80.0,
            hazards,
            DateTime.UtcNow
        );
    }

    public ParametricInsuranceTrigger EvaluateParametricClaim(
        string policyNumber,
        string metricType,
        double observedValue,
        double thresholdValue,
        decimal payoutAmount)
    {
        bool conditionMet = observedValue >= thresholdValue;
        return new ParametricInsuranceTrigger(
            $"TRIG-{Guid.NewGuid():N}"[..12],
            policyNumber,
            metricType,
            thresholdValue,
            observedValue,
            payoutAmount,
            conditionMet,
            DateTime.UtcNow
        );
    }

    public DisasterClaimSubmission CreateAutomatedDisasterClaim(
        string policyNumber,
        string incidentId,
        string damageCategory,
        decimal estimatedLoss,
        string merkleEvidenceHash,
        double lat,
        double lon)
    {
        var claimId = $"CLM-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var claim = new DisasterClaimSubmission(
            claimId,
            policyNumber,
            incidentId,
            damageCategory,
            estimatedLoss,
            merkleEvidenceHash,
            new List<string> { $"SHA256-{Guid.NewGuid():N}" },
            lat,
            lon,
            "AutoApproved",
            DateTime.UtcNow
        );

        _claims[claimId] = claim;
        return claim;
    }

    public IReadOnlyList<InsurancePolicyProfile> GetPoliciesInHazardZone(double centerLat, double centerLon, double radiusKm)
    {
        return _policies.Values.Where(p =>
        {
            double dLat = (p.InsuredLatitude - centerLat) * 111.0;
            double dLon = (p.InsuredLongitude - centerLon) * (111.0 * Math.Cos(centerLat * Math.PI / 180.0));
            double distanceKm = Math.Sqrt(dLat * dLat + dLon * dLon);
            return distanceKm <= radiusKm;
        }).ToList();
    }
}
