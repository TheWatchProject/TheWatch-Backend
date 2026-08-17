using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Security.Identity;

public enum ClearanceLevel { Civilian, VolunteerResponder, CertifiedEMT, FireCommand, TacticalCommander }

public class OidcEnterpriseFederationHandler
{
    private readonly ILogger<OidcEnterpriseFederationHandler> _logger;

    public OidcEnterpriseFederationHandler(ILogger<OidcEnterpriseFederationHandler> logger)
    {
        _logger = logger;
    }

    public Task<ClearanceLevel> ResolveClearanceFromClaimsAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var agencyClaim = principal.FindFirst("agency_id")?.Value;
        var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value ?? principal.FindFirst("roles")?.Value;

        var level = (agencyClaim, roleClaim) switch
        {
            (not null, "TacticalLead") => ClearanceLevel.TacticalCommander,
            (not null, "FireCaptain") => ClearanceLevel.FireCommand,
            (not null, "Paramedic") => ClearanceLevel.CertifiedEMT,
            (not null, _) => ClearanceLevel.VolunteerResponder,
            _ => ClearanceLevel.Civilian
        };

        _logger.LogInformation("Resolved clearance {Level} for user {User} (Agency: {Agency})", level, principal.Identity?.Name, agencyClaim ?? "Public");
        return Task.FromResult(level);
    }
}
