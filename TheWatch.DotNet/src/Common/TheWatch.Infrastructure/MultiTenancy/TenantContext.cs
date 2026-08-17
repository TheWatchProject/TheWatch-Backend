using System;
using Microsoft.AspNetCore.Http;
using ContractTenantContext = TheWatch.Contracts.MultiTenancy.ITenantContext;
using ContractTenantId = TheWatch.Contracts.MultiTenancy.TenantId;

namespace TheWatch.Infrastructure.MultiTenancy;

/// <summary>
/// Scoped service providing the resolved tenant identity for the current request.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the active tenant / agency identifier (e.g., POLICE_DEPT_SF, FIRE_DISTRICT_01, FEMA_REGION_9).
    /// </summary>
    string TenantId { get; }
}

/// <summary>
/// Resolves tenant identifier from HTTP headers (X-Tenant-Id), JWT claims, or subdomains.
/// </summary>
public class HttpTenantContext : ITenantContext, ContractTenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="HttpTenantContext"/>.
    /// </summary>
    /// <param name="httpContextAccessor">Http context accessor.</param>
    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the resolved tenant ID.
    /// </summary>
    public string TenantId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context != null && context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader))
            {
                return tenantHeader.ToString();
            }

            return "DEFAULT_AGENCY";
        }
    }

    /// <summary>Gets the explicitly resolved tenant without silently applying the legacy fallback.</summary>
    public ContractTenantId? CurrentTenant
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context is null) return null;
            if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader) &&
                ContractTenantId.TryParse(tenantHeader.ToString(), out var headerTenant))
            {
                return headerTenant;
            }

            var claimValue = context.User.FindFirst("tenant_id")?.Value ?? context.User.FindFirst("tid")?.Value;
            return ContractTenantId.TryParse(claimValue, out var claimTenant) ? claimTenant : null;
        }
    }

    /// <summary>Gets the resolved tenant or fails closed when the request has no tenant identity.</summary>
    public ContractTenantId RequireTenant() => CurrentTenant ?? throw new InvalidOperationException("No tenant identity was resolved for the current request.");
}
