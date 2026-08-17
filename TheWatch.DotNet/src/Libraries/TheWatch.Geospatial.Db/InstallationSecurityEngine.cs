using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TheWatch.Contracts;
using static TheWatch.Contracts.InstallationSecurityContracts;

namespace TheWatch.Geospatial.Db;

public interface IInstallationSecurityEngine
{
    void RegisterFacility(FacilityInstallation facility);
    FacilityInstallation? GetFacility(string facilityId);
    void AddSecurityZone(FacilitySecurityZone zone);
    void RegisterPersonnel(InstallationPersonnel person);
    IReadOnlyList<InstallationPersonnel> GetPersonnelByFacility(string facilityId);
    void UpdateThreatLevel(string facilityId, FacilityThreatLevel newLevel);
    FacilityMusterRoll GenerateMusterRoll(string facilityId);
    bool EvaluateZoneAccess(string personnelId, string zoneId, out string accessReason);
}

/// <summary>
/// Command & Control Engine for Physical Installation Security, Controlled Zones & Personnel Hierarchy.
/// </summary>
public sealed class InstallationSecurityEngine : IInstallationSecurityEngine
{
    private readonly ConcurrentDictionary<string, FacilityInstallation> _facilities = new();
    private readonly ConcurrentDictionary<string, List<FacilitySecurityZone>> _zones = new();
    private readonly ConcurrentDictionary<string, InstallationPersonnel> _personnel = new();

    public InstallationSecurityEngine()
    {
        SeedSampleInstallation();
    }

    private void SeedSampleInstallation()
    {
        var facility = new FacilityInstallation(
            "FAC-PRESIDIO-HQ",
            "Presidio Critical Operations Installation",
            FacilitySecurityTier.CriticalInfrastructure,
            37.7989,
            -122.4662,
            PerimeterRadiusMeters: 1200.0,
            PrimaryJurisdictionContact: "Federal Protective Service (FPS)",
            FacilityThreatLevel.FPCON_Normal,
            DateTime.UtcNow
        );
        _facilities[facility.FacilityId] = facility;

        var zones = new List<FacilitySecurityZone>
        {
            new FacilitySecurityZone("ZONE-GATE", facility.FacilityId, "Main Perimeter Guard Shack", RequiredClearanceLevel: 1, RequiresDualOfficerEscort: false, IsCurrentlyLockedDown: false),
            new FacilitySecurityZone("ZONE-LOBBY", facility.FacilityId, "Administration Public Lobby", RequiredClearanceLevel: 1, RequiresDualOfficerEscort: false, IsCurrentlyLockedDown: false),
            new FacilitySecurityZone("ZONE-OPS", facility.FacilityId, "Command & Emergency Operations Center", RequiredClearanceLevel: 3, RequiresDualOfficerEscort: false, IsCurrentlyLockedDown: false),
            new FacilitySecurityZone("ZONE-SCIF", facility.FacilityId, "Secure Compartmented Intelligence Vault (SCIF)", RequiredClearanceLevel: 5, RequiresDualOfficerEscort: true, IsCurrentlyLockedDown: false)
        };
        _zones[facility.FacilityId] = zones;

        // Register Command Staff, Officers, Staff, Inspectors, and Off-Site Customer
        var people = new List<InstallationPersonnel>
        {
            new InstallationPersonnel("P-001", facility.FacilityId, "Col. Marcus Vance", FacilityPersonnelRole.InstallationCommander, ClearanceLevel: 5, "BADGE-IC-01", "ZONE-OPS", "Facility Command Staff", IsCurrentlyOnSite: true, DateTime.UtcNow),
            new InstallationPersonnel("P-002", facility.FacilityId, "Elena Rostova", FacilityPersonnelRole.ChiefSecurityOfficer, ClearanceLevel: 5, "BADGE-CSO-01", "ZONE-OPS", "Facility Security Force", IsCurrentlyOnSite: true, DateTime.UtcNow),
            new InstallationPersonnel("P-003", facility.FacilityId, "Sgt. David Chen", FacilityPersonnelRole.WatchCommander, ClearanceLevel: 4, "BADGE-WC-04", "ZONE-GATE", "Facility Security Force", IsCurrentlyOnSite: true, DateTime.UtcNow),
            new InstallationPersonnel("P-004", facility.FacilityId, "Sarah Jenkins", FacilityPersonnelRole.FacilityStaffOrWorker, ClearanceLevel: 2, "BADGE-EMP-442", "ZONE-LOBBY", "Operations Engineering", IsCurrentlyOnSite: true, DateTime.UtcNow),
            new InstallationPersonnel("P-005", facility.FacilityId, "Dr. Arthur Ramos", FacilityPersonnelRole.ExternalInspector, ClearanceLevel: 3, "BADGE-INSP-OSHA", "ZONE-OPS", "OSHA Federal Inspection Team", IsCurrentlyOnSite: true, DateTime.UtcNow),
            new InstallationPersonnel("P-006", facility.FacilityId, "Rachel Sterling", FacilityPersonnelRole.OffSiteCustomerOrClient, ClearanceLevel: 2, "BADGE-CLIENT-09", "OFFSITE", "Global Enterprise Stakeholder", IsCurrentlyOnSite: false, DateTime.UtcNow)
        };

        foreach (var p in people)
        {
            _personnel[p.PersonnelId] = p;
        }
    }

    public void RegisterFacility(FacilityInstallation facility) => _facilities[facility.FacilityId] = facility;

    public FacilityInstallation? GetFacility(string facilityId) => _facilities.GetValueOrDefault(facilityId);

    public void AddSecurityZone(FacilitySecurityZone zone)
    {
        _zones.AddOrUpdate(
            zone.FacilityId,
            new List<FacilitySecurityZone> { zone },
            (_, list) => { lock (list) { list.Add(zone); return list; } }
        );
    }

    public void RegisterPersonnel(InstallationPersonnel person) => _personnel[person.PersonnelId] = person;

    public IReadOnlyList<InstallationPersonnel> GetPersonnelByFacility(string facilityId) =>
        _personnel.Values.Where(p => p.FacilityId == facilityId).ToList();

    public void UpdateThreatLevel(string facilityId, FacilityThreatLevel newLevel)
    {
        if (_facilities.TryGetValue(facilityId, out var fac))
        {
            _facilities[facilityId] = fac with { CurrentThreatLevel = newLevel };

            // If FPCON Charlie or Delta, automatically lock down high-security zones
            if (newLevel is FacilityThreatLevel.FPCON_Charlie or FacilityThreatLevel.FPCON_Delta)
            {
                if (_zones.TryGetValue(facilityId, out var zoneList))
                {
                    lock (zoneList)
                    {
                        for (int i = 0; i < zoneList.Count; i++)
                        {
                            if (zoneList[i].RequiredClearanceLevel >= 3)
                            {
                                zoneList[i] = zoneList[i] with { IsCurrentlyLockedDown = true };
                            }
                        }
                    }
                }
            }
        }
    }

    public FacilityMusterRoll GenerateMusterRoll(string facilityId)
    {
        var onSitePersonnel = _personnel.Values.Where(p => p.FacilityId == facilityId && p.IsCurrentlyOnSite).ToList();
        
        // Check unaccounted if check-in is older than 2 hours or pending verification
        var unaccounted = onSitePersonnel.Where(p => (DateTime.UtcNow - p.LastCheckInUtc).TotalHours > 2.0).Select(p => p.PersonnelId).ToList();
        int accounted = onSitePersonnel.Count - unaccounted.Count;

        return new FacilityMusterRoll(
            $"MUSTER-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            facilityId,
            TotalPersonnelExpected: onSitePersonnel.Count,
            AccountedForCount: accounted,
            MissingOrUnaccountedCount: unaccounted.Count,
            unaccounted,
            DateTime.UtcNow
        );
    }

    public bool EvaluateZoneAccess(string personnelId, string zoneId, out string accessReason)
    {
        accessReason = string.Empty;
        if (!_personnel.TryGetValue(personnelId, out var person))
        {
            accessReason = "Personnel record not found in installation database.";
            return false;
        }

        var allZones = _zones.Values.SelectMany(z => z).ToList();
        var targetZone = allZones.FirstOrDefault(z => z.ZoneId == zoneId);
        if (targetZone == null)
        {
            accessReason = "Security zone not found.";
            return false;
        }

        if (targetZone.IsCurrentlyLockedDown && person.Role != FacilityPersonnelRole.InstallationCommander && person.Role != FacilityPersonnelRole.ChiefSecurityOfficer)
        {
            accessReason = $"Zone [{targetZone.ZoneName}] is under ACTIVE LOCKDOWN. Access denied.";
            return false;
        }

        if (person.ClearanceLevel < targetZone.RequiredClearanceLevel)
        {
            accessReason = $"Insufficient clearance. Required Level {targetZone.RequiredClearanceLevel}, person has Level {person.ClearanceLevel}.";
            return false;
        }

        accessReason = $"Access granted to [{targetZone.ZoneName}] for {person.FullName} ({person.Role}).";
        return true;
    }
}
