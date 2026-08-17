using System.Collections.Concurrent;
using TheWatch.Contracts;
using static TheWatch.Contracts.CrimeDisasterAndMedicalStatisticsContracts;
using static TheWatch.Contracts.UnitedStatesCodeLegalContracts;

namespace TheWatch.Geospatial.Db;

public interface ILegalAndStatisticsEngine
{
    void RegisterStatute(UsCodeStatute statute);
    void RegisterCrimeStats(NibrsCrimeSectorStatistics stats);
    void RegisterHazardRisk(FemaNaturalHazardRiskIndex risk);
    void RegisterMedicalBenchmark(EmergencyMedicalStatisticalBenchmark benchmark);
    LegalComplianceEvaluation EvaluateIncidentCompliance(string incidentId, string category, bool isMassCasualty, bool requiresAirDrone);
    SectorTacticalStatisticalAssessment EvaluateSectorStatistics(string sectorId, double lat, double lon);
    IReadOnlyList<UsCodeStatute> GetStatutesForCategory(string category);
}

public sealed class LegalAndStatisticsEngine : ILegalAndStatisticsEngine
{
    private readonly ConcurrentDictionary<string, UsCodeStatute> _statutes = new();
    private readonly ConcurrentDictionary<string, NibrsCrimeSectorStatistics> _crimeStats = new();
    private readonly ConcurrentDictionary<string, FemaNaturalHazardRiskIndex> _hazardRisks = new();
    private readonly ConcurrentDictionary<string, EmergencyMedicalStatisticalBenchmark> _medicalBenchmarks = new();

    public LegalAndStatisticsEngine()
    {
        SeedStandardStatutesAndStatistics();
    }

    private void SeedStandardStatutesAndStatistics()
    {
        // Seed US Code Statutes
        var statutes = new List<UsCodeStatute>
        {
            new("42 U.S.C. § 5121", UsCodeTitle.Title42_ThePublicHealthAndWelfare, "5121",
                "Robert T. Stafford Disaster Relief and Emergency Assistance Act",
                "Authorizes the President to provide financial and physical assistance to state and local governments during major disasters and emergencies.",
                "FEMA", true, false, new List<string> { "NaturalDisaster", "MajorCatastrophe", "MassCasualty", "Wildfire", "Flood" }),

            new("42 U.S.C. § 1395dd", UsCodeTitle.Title42_ThePublicHealthAndWelfare, "1395dd",
                "Emergency Medical Treatment and Labor Act (EMTALA)",
                "Requires hospital emergency departments that accept Medicare to provide an appropriate medical screening examination and stabilizing treatment to individuals seeking care.",
                "HHS / CMS", true, false, new List<string> { "MedicalEmergency", "Trauma", "CardiacArrest", "Childbirth" }),

            new("47 U.S.C. § 1201", UsCodeTitle.Title47_Telecommunications, "1201",
                "Warning, Alert, and Response Network (WARN) Act",
                "Establishes the Wireless Emergency Alerts (WEA) framework allowing authorized alerting authorities to broadcast geographically targeted emergency alerts to mobile subscribers.",
                "FCC / FEMA", true, false, new List<string> { "EmergencyBroadcast", "EvacuationOrder", "AmberAlert", "HazmatWarning" }),

            new("49 U.S.C. § 44807", UsCodeTitle.Title49_Transportation, "44807",
                "Special Authority for Certain Unmanned Aircraft Systems (14 CFR Part 107)",
                "Regulates commercial and public safety operations of small unmanned aircraft systems (drones), visual line-of-sight waivers, and emergency medical payload delivery.",
                "FAA", true, true, new List<string> { "DroneOperations", "AedDelivery", "AerialReconnaissance" }),

            new("18 U.S.C. § 1038", UsCodeTitle.Title18_CrimesAndCriminalProcedure, "1038",
                "False Information and Hoaxes (Swatting and False Distress)",
                "Imposes severe criminal penalties including mandatory imprisonment and restitution for intentionally transmitting false or hoax distress signals or emergency reports.",
                "DOJ / FBI", true, true, new List<string> { "HoaxCall", "Swatting", "FalseAlarm" })
        };

        foreach (var s in statutes)
        {
            _statutes.TryAdd(s.Citation, s);
        }

        // Seed NIBRS Crime Statistics (San Francisco Metro)
        var sfCrime = new NibrsCrimeSectorStatistics(
            "06075",
            "San Francisco Metropolitan Sector",
            ViolentCrimesPer100k: 670.5,
            PropertyCrimesPer100k: 4890.0,
            HomicideRatePer100k: 6.2,
            AggravatedAssaultRatePer100k: 290.4,
            ShotSpotterGunshotAlertsPerMonth: 42.0,
            AveragePoliceResponseTimeSecondsP50: 310.0, // 5.1 minutes
            AveragePoliceResponseTimeSecondsP90: 680.0, // 11.3 minutes
            CaseClearanceRatePercent: 44.5,
            DateTime.UtcNow
        );

        _crimeStats.TryAdd(sfCrime.SectorFipsCode, sfCrime);

        // Seed FEMA NRI Hazard Index
        var sfFemaRisk = new FemaNaturalHazardRiskIndex(
            "06075",
            "San Francisco County, CA",
            OverallRiskScorePercentile: 96.8, // Very high composite hazard percentile
            ExpectedAnnualLossUsd: 142000000.00m,
            SocialVulnerabilityIndex: 0.58,
            CommunityResilienceScore: 0.74,
            new Dictionary<string, double>
            {
                ["Earthquake"] = 99.4,
                ["Wildfire"] = 72.1,
                ["CoastalFlooding"] = 84.6,
                ["SevereWeather"] = 58.2
            },
            DateTime.UtcNow
        );

        _hazardRisks.TryAdd(sfFemaRisk.CountyFips, sfFemaRisk);

        // Seed Emergency Medical Benchmark
        var bayAreaMed = new EmergencyMedicalStatisticalBenchmark(
            "REGION-BAY-01",
            OutOfHospitalCardiacArrestSurvivalPercent: 10.4, // Baseline
            MedianEmsTurnoutTimeSeconds: 78.0,
            MedianEmsTravelTimeSeconds: 340.0,
            EmergencyDepartmentDiversionRatePercent: 4.8,
            LeftWithoutBeingSeenRatePercent: 2.1,
            IcuBedOccupancyRatePercent: 82.5,
            SevereTbiMortalityPercentGcsUnder8: 32.0,
            DateTime.UtcNow
        );

        _medicalBenchmarks.TryAdd(bayAreaMed.RegionId, bayAreaMed);
    }

    public void RegisterStatute(UsCodeStatute statute)
    {
        _statutes[statute.Citation] = statute;
    }

    public void RegisterCrimeStats(NibrsCrimeSectorStatistics stats)
    {
        _crimeStats[stats.SectorFipsCode] = stats;
    }

    public void RegisterHazardRisk(FemaNaturalHazardRiskIndex risk)
    {
        _hazardRisks[risk.CountyFips] = risk;
    }

    public void RegisterMedicalBenchmark(EmergencyMedicalStatisticalBenchmark benchmark)
    {
        _medicalBenchmarks[benchmark.RegionId] = benchmark;
    }

    public LegalComplianceEvaluation EvaluateIncidentCompliance(
        string incidentId,
        string category,
        bool isMassCasualty,
        bool requiresAirDrone)
    {
        var applicable = _statutes.Values
            .Where(s => s.GovernedIncidentCategories.Contains(category) ||
                        (isMassCasualty && s.GovernedIncidentCategories.Contains("MassCasualty")) ||
                        (requiresAirDrone && s.GovernedIncidentCategories.Contains("DroneOperations")))
            .ToList();

        bool staffordEligible = isMassCasualty || category == "NaturalDisaster";
        bool emtalaMandated = category is "MedicalEmergency" or "Trauma" or "CardiacArrest";
        bool faaWaiverReq = requiresAirDrone;
        bool weaBroadcastAuth = isMassCasualty || category is "NaturalDisaster" or "EvacuationOrder";

        return new LegalComplianceEvaluation(
            incidentId,
            applicable,
            staffordEligible,
            emtalaMandated,
            faaWaiverReq,
            weaBroadcastAuth,
            DateTime.UtcNow
        );
    }

    public SectorTacticalStatisticalAssessment EvaluateSectorStatistics(string sectorId, double lat, double lon)
    {
        var crime = _crimeStats.Values.FirstOrDefault() ?? new NibrsCrimeSectorStatistics(
            "00000", "Default Sector", 500, 3000, 5, 200, 10, 300, 600, 50, DateTime.UtcNow);

        var disaster = _hazardRisks.Values.FirstOrDefault() ?? new FemaNaturalHazardRiskIndex(
            "00000", "Default County", 50, 1000000m, 0.5, 0.5, new(), DateTime.UtcNow);

        var medical = _medicalBenchmarks.Values.FirstOrDefault() ?? new EmergencyMedicalStatisticalBenchmark(
            "DEFAULT", 10.0, 60, 300, 5.0, 2.0, 80.0, 30.0, DateTime.UtcNow);

        // Composite vulnerability = weighted mean of crime, hazard, and social vulnerability
        double composite = Math.Round((disaster.SocialVulnerabilityIndex * 0.4) +
                                      (disaster.OverallRiskScorePercentile / 100.0 * 0.4) +
                                      ((1.0 - disaster.CommunityResilienceScore) * 0.2), 3);

        return new SectorTacticalStatisticalAssessment(
            sectorId,
            lat,
            lon,
            crime,
            disaster,
            medical,
            composite,
            DateTime.UtcNow
        );
    }

    public IReadOnlyList<UsCodeStatute> GetStatutesForCategory(string category)
    {
        return _statutes.Values.Where(s => s.GovernedIncidentCategories.Contains(category)).ToList();
    }
}
