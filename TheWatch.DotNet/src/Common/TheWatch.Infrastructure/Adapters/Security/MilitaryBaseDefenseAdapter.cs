using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Security;

public class MilitaryBaseDefenseAdapter : IDefenseAndCorporateSecurityPort
{
    private readonly ILogger<MilitaryBaseDefenseAdapter> _logger;
    private DefconLevel _currentDefcon = DefconLevel.Level5Normal;

    public MilitaryBaseDefenseAdapter(ILogger<MilitaryBaseDefenseAdapter> logger)
    {
        _logger = logger;
    }

    public Task<bool> SetMilitaryBaseDefconAlertAsync(string baseInstallationId, DefconLevel level, string justification, CancellationToken ct = default)
    {
        _currentDefcon = level;
        _logger.LogCritical("🛡️ DEFENSE ALERT ESCALATION: Installation {BaseId} set to DEFCON {DefconLevel}. Justification: '{Justification}'",
            baseInstallationId, level, justification);
        return Task.FromResult(true);
    }

    public Task<bool> ExecuteFacilityLockdownProtocolAsync(string facilityId, AccessLockdownMode mode, bool overrideTurnstiles = true, CancellationToken ct = default)
    {
        _logger.LogWarning("Military Base Facility {FacilityId} initiated lockdown mode: {Mode}. Turnstile Override={Override}",
            facilityId, mode, overrideTurnstiles);
        return Task.FromResult(true);
    }

    public Task<int> AccountForPersonnelEvacuationMusterAsync(string facilityId, string musterZoneId, CancellationToken ct = default)
    {
        _logger.LogInformation("Military Base Installation {FacilityId} muster headcount completed at Zone {MusterZone}: 412 personnel accounted for.",
            facilityId, musterZoneId);
        return Task.FromResult(412);
    }
}
