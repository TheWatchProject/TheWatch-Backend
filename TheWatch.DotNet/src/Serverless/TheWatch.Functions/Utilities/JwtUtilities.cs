using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Azure.Functions.Worker.Http;

namespace TheWatch.Functions.Utilities;

/// <summary>
/// Utility methods for extracting JWT claims from HTTP requests.
/// </summary>
public static class JwtUtilities
{
    /// <summary>
    /// Extracts the user ID from the Bearer token in the Authorization header.
    /// </summary>
    /// <param name="req">The HTTP request</param>
    /// <returns>The user ID if found and valid, otherwise null</returns>
    public static Guid? ExtractUserIdFromToken(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Authorization", out var authHeaderValues))
        {
            return null;
        }

        var authHeader = authHeaderValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            // Extract the 'sub' claim which contains the user ID
            var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);
            if (subClaim == null)
            {
                return null;
            }

            if (Guid.TryParse(subClaim.Value, out Guid userId))
            {
                return userId;
            }

            return null;
        }
        catch
        {
            // If token parsing fails, return null
            return null;
        }
    }

    /// <summary>
    /// Extracts all roles from the Bearer token.
    /// </summary>
    /// <param name="req">The HTTP request</param>
    /// <returns>Array of role strings, or empty array if none found</returns>
    public static string[] ExtractRolesFromToken(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Authorization", out var authHeaderValues))
        {
            return Array.Empty<string>();
        }

        var authHeader = authHeaderValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Array.Empty<string>();
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            // Extract all role claims
            var roleClaims = jwtToken.Claims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                .Select(c => c.Value)
                .ToArray();

            return roleClaims;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Checks if the token contains a specific role.
    /// </summary>
    /// <param name="req">The HTTP request</param>
    /// <param name="role">The role to check for (e.g., "hq", "admin", "responder")</param>
    /// <returns>True if the role is present, false otherwise</returns>
    public static bool HasRole(HttpRequestData req, string role)
    {
        var roles = ExtractRolesFromToken(req);
        return roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the token contains any of the specified roles.
    /// </summary>
    /// <param name="req">The HTTP request</param>
    /// <param name="roles">The roles to check for</param>
    /// <returns>True if any role is present, false otherwise</returns>
    public static bool HasAnyRole(HttpRequestData req, params string[] roles)
    {
        var userRoles = ExtractRolesFromToken(req);
        return roles.Any(role => userRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts a custom claim from the token.
    /// </summary>
    /// <param name="req">The HTTP request</param>
    /// <param name="claimType">The claim type to extract</param>
    /// <returns>The claim value if found, otherwise null</returns>
    public static string? ExtractClaim(HttpRequestData req, string claimType)
    {
        if (!req.Headers.TryGetValues("Authorization", out var authHeaderValues))
        {
            return null;
        }

        var authHeader = authHeaderValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var claim = jwtToken.Claims.FirstOrDefault(c => c.Type == claimType);
            return claim?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validates and parses JWT from HttpHeadersCollection.
    /// </summary>
    public static ClaimsPrincipal? ValidateJwtFromHeader(HttpHeadersCollection? headers)
    {
        if (headers == null || !headers.TryGetValues("Authorization", out var authHeaderValues))
        {
            return null;
        }

        var authHeader = authHeaderValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            var identity = new ClaimsIdentity(jwtToken.Claims, "Bearer");
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the user ID from a ClaimsPrincipal.
    /// </summary>
    public static Guid? GetUserIdFromClaims(ClaimsPrincipal? claims)
    {
        if (claims == null) return null;
        var subClaim = claims.FindFirst(JwtRegisteredClaimNames.Sub) ?? claims.FindFirst(ClaimTypes.NameIdentifier);
        if (subClaim != null && Guid.TryParse(subClaim.Value, out var id))
        {
            return id;
        }
        return null;
    }

    /// <summary>
    /// Checks if a ClaimsPrincipal has a specific role.
    /// </summary>
    public static bool HasRole(ClaimsPrincipal? claims, string role)
    {
        if (claims == null) return false;
        return claims.FindAll(ClaimTypes.Role).Concat(claims.FindAll("role")).Any(c => string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase));
    }
}
