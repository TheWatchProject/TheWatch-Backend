// Copyright (c) TheWatch. Licensed under MIT.
//
// JWT bearer authentication wired up as a single opt-in call. The configuration
// is read from the standard "TheWatch:Auth" section so callers don't have to
// repeat JWT validation parameters everywhere. Health-check endpoints are
// excluded so liveness probes keep working when a token isn't attached.

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TheWatch.Infrastructure.Security;

public sealed class TheWatchAuthOptions
{
    public const string SectionName = "TheWatch:Auth";

    /// <summary>Issuer URL (e.g. "https://login.microsoftonline.com/{tenant}/v2.0").</summary>
    public string? Issuer { get; set; }

    /// <summary>Audience (the API app ID URI or GUID).</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Optional symmetric signing key for development/testing. Production
    /// deployments should leave this null and configure a JWKS endpoint instead.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>JWKS metadata URL for production Entra ID / Auth0 / Okta issuers.</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Whether to require HTTPS metadata (default true).</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Health-check paths that bypass authentication.</summary>
    public string[] AnonymousPaths { get; set; } = new[] { "/health", "/alive" };
}

public static class TheWatchAuthenticationExtensions
{
    /// <summary>
    /// Adds JWT bearer authentication wired against <see cref="TheWatchAuthOptions"/>.
    /// Throws <see cref="OptionsValidationException"/> at startup if the issuer or
    /// audience is missing, because an unauthenticated API is never an acceptable
    /// default in this codebase.
    /// </summary>
    public static IServiceCollection AddTheWatchAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TheWatchAuthOptions>()
            .Bind(configuration.GetSection(TheWatchAuthOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer),
                "TheWatch:Auth:Issuer is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience),
                "TheWatch:Auth:Audience is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) || !string.IsNullOrWhiteSpace(o.MetadataAddress),
                "Either TheWatch:Auth:SigningKey or TheWatch:Auth:MetadataAddress must be set.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Configure JwtBearerOptions from our strongly-typed options. Done via
        // PostConfigure so we don't capture an IServiceProvider at registration
        // time (which would break Options validation against the same provider).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<TheWatchAuthOptions>>((bearer, authOptionsAccessor) =>
            {
                var auth = authOptionsAccessor.Value;

                bearer.RequireHttpsMetadata = auth.RequireHttpsMetadata;
                bearer.MapInboundClaims = false;

                if (!string.IsNullOrWhiteSpace(auth.MetadataAddress))
                {
                    bearer.MetadataAddress = auth.MetadataAddress;
                }

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = auth.Issuer,
                    ValidAudience = auth.Audience,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles",
                };

                if (!string.IsNullOrWhiteSpace(auth.SigningKey))
                {
                    bearer.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey));
                }
            });

        services.AddAuthorization(options =>
        {
            // Default policy: every endpoint requires an authenticated user
            // unless explicitly marked [AllowAnonymous] or listed in
            // TheWatchAuthOptions.AnonymousPaths.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    /// <summary>
    /// Adds the authentication + authorization middleware. Use only after
    /// UseRouting() so endpoint metadata is available.
    /// </summary>
    public static IApplicationBuilder UseTheWatchAuthentication(this IApplicationBuilder app)
    {
        app.Use(async (ctx, next) =>
        {
            var config = ctx.RequestServices.GetService<IConfiguration>();
            var anonymousPaths = config?
                .GetSection(TheWatchAuthOptions.SectionName)
                .GetSection("AnonymousPaths")
                .Get<string[]>() ?? new[] { "/health", "/alive" };

            var path = ctx.Request.Path.Value ?? string.Empty;
            if (Array.IndexOf(anonymousPaths, path) >= 0)
            {
                var endpoint = ctx.GetEndpoint();
                var metadata = new List<object> { new AllowAnonymousAttribute() };
                if (endpoint?.Metadata != null)
                {
                    metadata.AddRange(endpoint.Metadata);
                }
                ctx.SetEndpoint(new Endpoint(
                    endpoint?.RequestDelegate,
                    new EndpointMetadataCollection(metadata),
                    endpoint?.DisplayName ?? "AnonymousEndpoint"));
            }
            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}

/// <summary>
/// Alias for <see cref="AuthorizeAttribute"/> that signals the intent
/// "this endpoint requires an authenticated TheWatch caller". Use this
/// instead of [Authorize] in service code so the policy intent is searchable.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TheWatchAuthorizeAttribute : AuthorizeAttribute
{
    public TheWatchAuthorizeAttribute() : base() { }
    public TheWatchAuthorizeAttribute(string policy) : base(policy) { }
}
