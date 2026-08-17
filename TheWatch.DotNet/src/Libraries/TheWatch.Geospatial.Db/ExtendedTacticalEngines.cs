using System.Collections.Concurrent;
using TheWatch.Contracts;
using static TheWatch.Contracts.FamilyHealthContracts;
using static TheWatch.Contracts.GamificationContracts;
using static TheWatch.Contracts.SituationalAuthorityDslContracts;
using static TheWatch.Contracts.SurveillanceAndCctvContracts;
using static TheWatch.Contracts.TelehealthDoctorContracts;

namespace TheWatch.Geospatial.Db;

public interface IExtendedTacticalEngines
{
    // Family Health
    void RegisterFamilyMember(FamilyCircleMember member);
    void RegisterSafeZone(FamilySafeZone zone);
    FamilySafetyAlert? EvaluateFamilyGeofence(string memberId, double lat, double lon);

    // Telehealth
    void RegisterPhysician(TelehealthPhysicianProfile physician);
    TelehealthConsultationSession CreateConsultationSession(string incidentId, string casualtyId, string medicId);
    EmergencyPrescriptionOrder IssueEmergencyPrescription(string sessionId, string casualtyId, string physicianId, string med, string dose, string indication);

    // Gamification
    CitizenHeroProfile AwardResponderXp(string userId, int xpToAdd, string reason);
    IReadOnlyList<NeighborhoodLeaderboardRow> GetLeaderboard();

    // Surveillance
    void RegisterCamera(GeotaggedCctvCamera camera);
    IReadOnlyList<GeotaggedCctvCamera> FindCamerasNearCoordinate(double lat, double lon, double radiusMeters = 500.0);

    // Situational Authority DSL
    void RegisterRule(SituationalAuthorityRule rule);
    IncidentCommandHierarchy ResolveCommandHierarchy(string incidentId, string category, bool isMultiJurisdictional);
}

public sealed class ExtendedTacticalEngines : IExtendedTacticalEngines
{
    private readonly ConcurrentDictionary<string, FamilyCircleMember> _familyMembers = new();
    private readonly ConcurrentDictionary<string, FamilySafeZone> _safeZones = new();
    private readonly ConcurrentDictionary<string, TelehealthPhysicianProfile> _physicians = new();
    private readonly ConcurrentDictionary<string, CitizenHeroProfile> _heroProfiles = new();
    private readonly ConcurrentDictionary<string, GeotaggedCctvCamera> _cameras = new();
    private readonly ConcurrentDictionary<string, SituationalAuthorityRule> _rules = new();

    public ExtendedTacticalEngines()
    {
        SeedStandardTacticalData();
    }

    private void SeedStandardTacticalData()
    {
        // 1. Seed Family Health
        var member = new FamilyCircleMember(
            "FAM-MEMBER-01",
            "CIRCLE-SF-100",
            "Eleanor Vance (Grandmother)",
            "Dependent",
            DependentVulnerabilityLevel.ElderlyFallRisk,
            "+1-415-555-0199",
            37.7749,
            -122.4194,
            DateTime.UtcNow
        );
        _familyMembers.TryAdd(member.MemberId, member);

        var zone = new FamilySafeZone(
            "ZONE-HOME-01",
            "CIRCLE-SF-100",
            "Home Safe Zone",
            37.7749,
            -122.4194,
            RadiusMeters: 200.0,
            NotifyOnExit: true,
            NotifyOnEntry: true
        );
        _safeZones.TryAdd(zone.ZoneId, zone);

        // 2. Seed Telehealth Physician
        var doctor = new TelehealthPhysicianProfile(
            "DOC-MD-882",
            "Dr. Sarah Lin, MD, FACS",
            "CA-MD-994821",
            "TraumaSurgery",
            IsCurrentlyOnCall: true,
            "San Francisco General Trauma Center",
            DateTime.UtcNow.AddHours(8)
        );
        _physicians.TryAdd(doctor.PhysicianId, doctor);

        // 3. Seed Gamification Profile
        var hero = new CitizenHeroProfile(
            "USER-HERO-01",
            "CitizenMedic_SF",
            ExperiencePoints: 1250,
            HeroLevel: 4,
            VerifiedEmergenciesAssisted: 5,
            CprTrainingHoursVerified: 12,
            new List<string> { "CPR Certified", "First on Scene", "AED Responder" },
            DateTime.UtcNow.AddYears(-1)
        );
        _heroProfiles.TryAdd(hero.UserId, hero);

        // 4. Seed CCTV Camera
        var camera = new GeotaggedCctvCamera(
            "CAM-SF-MARKET-01",
            "Market & 4th St Public Safety Cam #1",
            "rtsp://cameras.sfgov.org/live/market4th_hd",
            CameraStreamProtocol.ONVIF_ProfileS,
            37.7750,
            -122.4190,
            CoverageRadiusMeters: 150.0,
            HeadingDegrees: 45,
            HasPtzControl: true,
            IsPublicSafetyAuthorized: true
        );
        _cameras.TryAdd(camera.CameraId, camera);

        // 5. Seed Situational Authority DSL Rule
        var rule = new SituationalAuthorityRule(
            "RULE-HAZMAT-01",
            "SAR-HAZMAT-COMMAND",
            "HazmatExplosion",
            JurisdictionalTier.CountySheriff,
            new List<JurisdictionalTier> { JurisdictionalTier.MunicipalPolice, JurisdictionalTier.StateHighwayPatrol, JurisdictionalTier.FederalAgency },
            "IF HazmatClass in [Explosives, Toxics] AND Radius > 500m THEN ESCALATE TO State/Federal",
            "42 U.S.C. § 7412(r) / EPCRA",
            AutomaticMutualAidTriggered: true
        );
        _rules.TryAdd(rule.RuleId, rule);
    }

    public void RegisterFamilyMember(FamilyCircleMember member) => _familyMembers[member.MemberId] = member;
    public void RegisterSafeZone(FamilySafeZone zone) => _safeZones[zone.ZoneId] = zone;

    public FamilySafetyAlert? EvaluateFamilyGeofence(string memberId, double lat, double lon)
    {
        if (!_familyMembers.TryGetValue(memberId, out var member)) return null;

        var memberZones = _safeZones.Values.Where(z => z.FamilyCircleId == member.FamilyCircleId).ToList();
        foreach (var zone in memberZones)
        {
            double dLat = (lat - zone.CenterLatitude) * 111000.0;
            double dLon = (lon - zone.CenterLongitude) * (111000.0 * Math.Cos(lat * Math.PI / 180.0));
            double distMeters = Math.Sqrt(dLat * dLat + dLon * dLon);

            if (distMeters > zone.RadiusMeters && zone.NotifyOnExit)
            {
                return new FamilySafetyAlert(
                    $"ALERT-FAM-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                    member.FamilyCircleId,
                    member.MemberId,
                    member.FullName,
                    "SafeZoneExit",
                    $"{member.FullName} has exited '{zone.ZoneName}' boundary ({distMeters:F0}m from center).",
                    lat,
                    lon,
                    DateTime.UtcNow
                );
            }
        }
        return null;
    }

    public void RegisterPhysician(TelehealthPhysicianProfile physician) => _physicians[physician.PhysicianId] = physician;

    public TelehealthConsultationSession CreateConsultationSession(string incidentId, string casualtyId, string medicId)
    {
        var doc = _physicians.Values.FirstOrDefault(p => p.IsCurrentlyOnCall) ?? _physicians.Values.First();
        var sessionId = $"SESSION-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        return new TelehealthConsultationSession(
            sessionId,
            incidentId,
            casualtyId,
            medicId,
            doc.PhysicianId,
            $"webrtc-room-{sessionId.ToLowerInvariant()}",
            "Connected",
            DateTime.UtcNow,
            DateTime.UtcNow,
            null
        );
    }

    public EmergencyPrescriptionOrder IssueEmergencyPrescription(
        string sessionId,
        string casualtyId,
        string physicianId,
        string med,
        string dose,
        string indication)
    {
        return new EmergencyPrescriptionOrder(
            $"RX-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            sessionId,
            casualtyId,
            physicianId,
            med,
            dose,
            indication,
            $"SIG-ECDSA-{Guid.NewGuid():N}"[..16],
            DateTime.UtcNow
        );
    }

    public CitizenHeroProfile AwardResponderXp(string userId, int xpToAdd, string reason)
    {
        var current = _heroProfiles.GetOrAdd(userId, id => new CitizenHeroProfile(
            id,
            $"Hero_{id[..6]}",
            0,
            1,
            0,
            0,
            new List<string>(),
            DateTime.UtcNow
        ));

        int newXp = current.ExperiencePoints + xpToAdd;
        int newLevel = 1 + (newXp / 500);
        var updated = current with
        {
            ExperiencePoints = newXp,
            HeroLevel = newLevel,
            VerifiedEmergenciesAssisted = current.VerifiedEmergenciesAssisted + 1
        };
        _heroProfiles[userId] = updated;
        return updated;
    }

    public IReadOnlyList<NeighborhoodLeaderboardRow> GetLeaderboard()
    {
        return _heroProfiles.Values
            .OrderByDescending(p => p.ExperiencePoints)
            .Select((p, idx) => new NeighborhoodLeaderboardRow(
                idx + 1,
                p.UserId,
                p.DisplayHandle,
                "San Francisco Central",
                p.ExperiencePoints,
                p.VerifiedEmergenciesAssisted
            ))
            .ToList();
    }

    public void RegisterCamera(GeotaggedCctvCamera camera) => _cameras[camera.CameraId] = camera;

    public IReadOnlyList<GeotaggedCctvCamera> FindCamerasNearCoordinate(double lat, double lon, double radiusMeters = 500.0)
    {
        return _cameras.Values.Where(c =>
        {
            double dLat = (c.Latitude - lat) * 111000.0;
            double dLon = (c.Longitude - lon) * (111000.0 * Math.Cos(lat * Math.PI / 180.0));
            double distMeters = Math.Sqrt(dLat * dLat + dLon * dLon);
            return distMeters <= radiusMeters;
        }).ToList();
    }

    public void RegisterRule(SituationalAuthorityRule rule) => _rules[rule.RuleId] = rule;

    public IncidentCommandHierarchy ResolveCommandHierarchy(string incidentId, string category, bool isMultiJurisdictional)
    {
        var matchedRule = _rules.Values.FirstOrDefault(r => r.IncidentCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
            ?? _rules.Values.First();

        var supporting = new List<string> { "San Francisco Police Dept", "SF Fire Dept", "California Highway Patrol" };
        if (isMultiJurisdictional)
        {
            supporting.Add("FEMA Region 9 Rapid Response");
            supporting.Add("FBI Joint Terrorism Task Force (JTTF)");
        }

        return new IncidentCommandHierarchy(
            incidentId,
            matchedRule.PrimaryLeadTier,
            "San Francisco County Incident Command Center",
            supporting,
            new List<SituationalAuthorityRule> { matchedRule },
            DateTime.UtcNow
        );
    }
}
