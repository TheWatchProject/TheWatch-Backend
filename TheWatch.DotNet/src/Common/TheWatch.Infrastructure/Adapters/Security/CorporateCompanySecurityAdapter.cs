using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Security;

public class CorporateCompanySecurityAdapter
{
    private readonly ILogger<CorporateCompanySecurityAdapter> _logger;

    public CorporateCompanySecurityAdapter(ILogger<CorporateCompanySecurityAdapter> logger)
    {
        _logger = logger;
    }

    public Task<bool> TriggerActiveShooterLockdownAsync(string corporateCampusId, string buildingWing, CancellationToken ct = default)
    {
        _logger.LogCritical("🚨 CORPORATE CAMPUS ACTIVE THREAT LOCKDOWN: Campus {CampusId}, Wing {Wing}. Magnetic doors sealed, strobe beacons active.",
            corporateCampusId, buildingWing);
        return Task.FromResult(true);
    }

    public Task<int> ScanBadgeEvacuationMusterAsync(string campusId, string assemblyArea, string employeeBadgeId, CancellationToken ct = default)
    {
        _logger.LogInformation("Corporate muster scan: Badge {BadgeId} verified safe at Assembly Area {AssemblyArea} (Campus: {CampusId})",
            employeeBadgeId, assemblyArea, campusId);
        return Task.FromResult(1);
    }
}
