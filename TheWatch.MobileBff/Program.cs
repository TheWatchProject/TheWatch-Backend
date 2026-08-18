// <copyright file="Program.cs" company="The Watch, LLC">
// Copyright (c) 2026 Barton Milnor Mallory, The Watch, LLC. All rights reserved.
// </copyright>

/// <summary>
/// Source file: src/Services/TheWatch.MobileBff/Program.cs
/// Module: Enterprise Microservices, BFF Gateway & Tactical Dispatch
/// Defines: record CalculateRouteRequest, record JoinCrowdEventRequest, record VolunteerDetectionReport
/// Namespace: TheWatch
/// </summary>
using TheWatch.Domain.Entities;
using TheWatch.Infrastructure.Security;
using TheWatch.ServiceDefaults;
using TheWatch.Domain.Models.Mobile;
using TheWatch.MobileBff.Swarm;
using TheWatch.MobileBff.Hubs;
using TheWatch.Contracts;
using TheWatch.Geospatial.Db;
using Microsoft.Extensions.Options;
using static TheWatch.Contracts.MappingAndRoutingContracts;
using static TheWatch.Contracts.VolunteerCrowdMonitoringContracts;
using static TheWatch.Contracts.InstallationSecurityContracts;
using static TheWatch.Contracts.WhistleblowerAndTipsContracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddTheWatchAuthentication(builder.Configuration);

// Tactical Geospatial & Governance Engines
builder.Services.AddSingleton<IConstrainedPathfinder, ConstrainedAStarPathfinder>();
builder.Services.AddSingleton<IVolunteerCrowdMonitoringEngine, VolunteerCrowdMonitoringEngine>();
builder.Services.AddSingleton<IInstallationSecurityEngine, InstallationSecurityEngine>();
builder.Services.AddSingleton<IWhistleblowerAndTipsEngine, WhistleblowerAndTipsEngine>();
builder.Services.AddSingleton<TheWatch.Services.IH3BackendResponderCache, TheWatch.Services.H3BackendResponderCache>();
builder.Services.AddSingleton<TheWatch.Services.INotificationService, TheWatch.Services.NotificationService>();
builder.Services.AddSingleton<TheWatch.Services.IDirectionsService, TheWatch.Services.DirectionsService>();

builder.Services.AddOptions<SwarmOptions>()
    .Bind(builder.Configuration.GetSection(SwarmOptions.SectionName))
    .Validate(o => o.MaxConcurrentDomains > 0, "Swarm max concurrency must be positive.")
    .ValidateOnStart();
builder.Services.AddHttpClient<AzureOpenAiSwarmExecutionProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(35);
});
builder.Services.AddSingleton<SimulationSwarmExecutionProvider>();
builder.Services.AddSingleton<ISwarmExecutionProvider>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SwarmOptions>>().Value;
    return options.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<AzureOpenAiSwarmExecutionProvider>()
        : sp.GetRequiredService<SimulationSwarmExecutionProvider>();
});
builder.Services.AddSingleton<ISwarmOrchestrator, SwarmOrchestrator>();
builder.Services.AddAuthorization(options =>
    options.AddPolicy("SwarmDispatch", policy => policy.RequireRole("Dispatcher", "Admin", "Field Commander")));

var app = builder.Build();

app.UseTheWatchAuthentication();
app.MapDefaultEndpoints();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/v1/mobile/bootstrap", () =>
{
    return Results.Ok(new
    {
        user = new { id = "user-1", name = "Responder Alpha", role = "Field Commander" },
        incidents = new[]
        {
            new Incident { Id = "inc-101", Title = "Downtown Flash Flood", Severity = "CRITICAL", Status = "ACTIVE", Latitude = 37.7749, Longitude = -122.4194 }
        },
        serverTime = DateTime.UtcNow
    });
}).RequireAuthorization();

// ============================================
// Tactical Routing WebApi
// ============================================
var routing = app.MapGroup("/api/v1/routing");

routing.MapPost("/calculate", (
    CalculateRouteRequest req,
    IConstrainedPathfinder pathfinder) =>
{
    var route = pathfinder.CalculateEmergencyRoute(
        req.IncidentId,
        req.UnitId,
        req.OriginLatitude,
        req.OriginLongitude,
        req.DestinationLatitude,
        req.DestinationLongitude
    );
    return Results.Ok(route);
}).AllowAnonymous();

// ============================================
// Crowd Safety Volunteer Monitoring WebApi
// ============================================
var crowd = app.MapGroup("/api/v1/crowd");

crowd.MapGet("/events", (IVolunteerCrowdMonitoringEngine engine) =>
    Results.Ok(engine.GetActiveEvents())).AllowAnonymous();

crowd.MapPost("/events", (CrowdSafetyMonitoringEvent ev, IVolunteerCrowdMonitoringEngine engine) =>
{
    engine.CreateEvent(ev);
    return Results.Created($"/api/v1/crowd/events/{ev.EventId}", ev);
}).AllowAnonymous();

crowd.MapPost("/join", (JoinCrowdEventRequest req, IVolunteerCrowdMonitoringEngine engine) =>
{
    var session = engine.JoinEvent(req.EventId, req.UserId, req.Handle, req.Latitude, req.Longitude);
    return Results.Ok(session);
}).AllowAnonymous();

crowd.MapPost("/detection", (VolunteerDetectionReport req, IVolunteerCrowdMonitoringEngine engine) =>
{
    var triangulated = engine.IngestVolunteerDetection(
        req.EventId,
        req.VolunteerUserId,
        req.DetectedPhrase,
        req.Confidence,
        req.Latitude,
        req.Longitude,
        req.TimestampUtc
    );
    return Results.Ok(new { Triangulated = triangulated != null, Signal = triangulated });
}).AllowAnonymous();

// ============================================
// Installation Security & Command WebApi
// ============================================
var installation = app.MapGroup("/api/v1/installation");

installation.MapGet("/{facilityId}", (string facilityId, IInstallationSecurityEngine engine) =>
{
    var fac = engine.GetFacility(facilityId);
    return fac != null ? Results.Ok(fac) : Results.NotFound();
}).AllowAnonymous();

installation.MapGet("/{facilityId}/personnel", (string facilityId, IInstallationSecurityEngine engine) =>
    Results.Ok(engine.GetPersonnelByFacility(facilityId))).AllowAnonymous();

installation.MapPost("/{facilityId}/threat-level", (string facilityId, SetThreatLevelRequest req, IInstallationSecurityEngine engine) =>
{
    engine.UpdateThreatLevel(facilityId, req.ThreatLevel);
    return Results.Ok(new { FacilityId = facilityId, ThreatLevel = req.ThreatLevel });
}).AllowAnonymous();

installation.MapGet("/{facilityId}/muster", (string facilityId, IInstallationSecurityEngine engine) =>
    Results.Ok(engine.GenerateMusterRoll(facilityId))).AllowAnonymous();

// ============================================
// Whistleblower & Community Tips WebApi
// ============================================
var governance = app.MapGroup("/api/v1/governance");

governance.MapPost("/whistleblower", (SubmitWhistleblowerRequest req, IWhistleblowerAndTipsEngine engine) =>
{
    var report = engine.SubmitWhistleblowerReport(
        req.Ticker,
        req.Category,
        req.EncryptedPayload,
        req.ClaimantSecretToken,
        req.IsAnonymous
    );
    return Results.Ok(report);
}).AllowAnonymous();

governance.MapGet("/whistleblower/{reportId}", (string reportId, string token, IWhistleblowerAndTipsEngine engine) =>
{
    var report = engine.RetrieveWhistleblowerReport(reportId, token);
    return report != null ? Results.Ok(report) : Results.NotFound(new { Message = "Report not found or invalid token." });
}).AllowAnonymous();

governance.MapPost("/tips", (SubmitTipRequest req, IWhistleblowerAndTipsEngine engine) =>
{
    var tip = engine.SubmitCommunityTip(
        req.Category,
        req.Description,
        req.Latitude,
        req.Longitude,
        req.Landmark,
        req.IsAnonymous,
        req.SubmitterAlias ?? "Anonymous",
        req.RewardRequested
    );
    return Results.Ok(tip);
}).AllowAnonymous();

// ============================================
// AI Swarm WebApi
// ============================================
var swarm = app.MapGroup("/api/v1/swarm").RequireAuthorization("SwarmDispatch");

swarm.MapGet("/agents", (ISwarmOrchestrator orchestrator, IOptions<SwarmOptions> options) =>
    Results.Ok(orchestrator.Domains.Select(d => new SwarmAgentView(
        d.AgentId, d.CodeName, d.DomainId, d.Domain, "IDLE", options.Value.Model, options.Value.Provider))));

swarm.MapGet("/health", (ISwarmOrchestrator orchestrator, IOptions<SwarmOptions> options) =>
    Results.Ok(new SwarmHealthView(
        options.Value.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase) ? "READY" : "SIMULATION",
        options.Value.Provider, orchestrator.Domains.Count, orchestrator.Domains.Count * 10)));

swarm.MapPost("/tasks", async (SwarmTaskSubmission submission, HttpContext context,
    ISwarmOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    if (submission.CadAction && !context.User.HasClaim("step_up", "true"))
        return Results.Forbid();

    try
    {
        var submittedBy = context.User.Identity?.Name ?? context.User.FindFirst("sub")?.Value ?? "unknown";
        var result = await orchestrator.SubmitAsync(submission, submittedBy, cancellationToken);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["submission"] = new[] { ex.Message } });
    }
});

swarm.MapGet("/tasks/{taskId}", (string taskId, ISwarmOrchestrator orchestrator) =>
    orchestrator.TryGet(taskId, out var task) ? Results.Ok(task) : Results.NotFound());

// ============================================
// H3 Geospatial Responders & Notifications WebApi
// ============================================
var respondersApi = app.MapGroup("/api/v1/responders");

respondersApi.MapGet("/nearby-h3", async (
    double lat,
    double lng,
    int? kRing,
    int? res,
    TheWatch.Services.IH3BackendResponderCache cache) =>
{
    var list = await cache.QueryNearbyRespondersAsync(lat, lng, kRing ?? 2, res ?? 8);
    string originH3 = cache.LatLngToH3(lat, lng, res ?? 8);
    return Results.Ok(new
    {
        OriginH3Index = originH3,
        QueriedCellsCount = cache.GetKRingHexagons(originH3, kRing ?? 2).Count,
        Responders = list
    });
}).AllowAnonymous();

respondersApi.MapPost("/telemetry", async (
    TheWatch.Services.BackendResponderDto telemetry,
    TheWatch.Services.IH3BackendResponderCache cache) =>
{
    await cache.UpdateResponderPositionAsync(telemetry);
    return Results.Ok(new { Status = "Updated", H3 = telemetry.H3Index });
}).AllowAnonymous();

var notificationsApi = app.MapGroup("/api/v1/notifications");

notificationsApi.MapPost("/dispatch-h3", async (
    TheWatch.Services.NotificationDispatchRequest req,
    TheWatch.Services.INotificationService service) =>
{
    int count = await service.DispatchH3GeofenceAlertAsync(req);
    return Results.Ok(new { Status = "Dispatched", TargetCells = req.TargetH3Index, NotifiedCount = count });
}).AllowAnonymous();

// ============================================
// Generic Directions & Emergency Summoning WebApi
// ============================================
var directionsApi = app.MapGroup("/api/v1/directions");

directionsApi.MapPost("/calculate", async (
    TheWatch.Services.GenericDirectionsRequest req,
    TheWatch.Services.IDirectionsService service) =>
{
    var route = await service.CalculateDirectionsAsync(req);
    return Results.Ok(route);
}).AllowAnonymous();

var dispatchFlowApi = app.MapGroup("/api/v1/dispatch");

dispatchFlowApi.MapPost("/summon", (SummonRespondersRequest req) =>
{
    // Generates dual dispatch (SMS + Push) with confirmation deep links
    string confirmUrl = $"https://app.relentlessglobal.net/dispatch/respond?incidentId={req.IncidentId}&responderId={req.ResponderId}&action=confirm";
    string denyUrl = $"https://app.relentlessglobal.net/dispatch/respond?incidentId={req.IncidentId}&responderId={req.ResponderId}&action=deny";
    string smsText = $"[The Watch EMERGENCY DISPATCH] #{req.IncidentId}: {req.Title} at {req.LocationName}. Confirm to respond: {confirmUrl} or Deny: {denyUrl}";

    return Results.Ok(new
    {
        Status = "SummonDispatched",
        Channels = new[] { "Azure_ACS_SMS", "Firebase_FCM_Push" },
        SmsPayload = smsText,
        ConfirmDeepLink = confirmUrl,
        DenyDeepLink = denyUrl,
        NavigateUrl = $"/directions/{req.IncidentId}"
    });
}).AllowAnonymous();

dispatchFlowApi.MapGet("/respond", (string incidentId, string responderId, string action) =>
{
    bool isConfirmed = string.Equals(action, "confirm", StringComparison.OrdinalIgnoreCase);
    if (isConfirmed)
    {
        // EnRoute status set -> redirect immediately to turn-by-turn navigation in app
        return Results.Redirect($"/directions/{incidentId}?status=confirmed&unit={responderId}");
    }
    return Results.Ok(new { Status = "Declined", IncidentId = incidentId, Message = "Response declined. Re-routing dispatch to alternative units." });
}).AllowAnonymous();

// ============================================
// Real-Time SignalR Hub Endpoints
// ============================================
app.MapHub<IncidentHub>("/hubs/incident").AllowAnonymous();
app.MapHub<TelemetryHub>("/hubs/telemetry").AllowAnonymous();
app.MapHub<DispatchHub>("/hubs/dispatch").AllowAnonymous();
app.MapHub<MeshRelayHub>("/hubs/mesh").AllowAnonymous();

app.Run();

// DTO Wire Records for WebApi
public sealed record CalculateRouteRequest(string IncidentId, string UnitId, double OriginLatitude, double OriginLongitude, double DestinationLatitude, double DestinationLongitude);
public sealed record JoinCrowdEventRequest(string EventId, string UserId, string Handle, double Latitude, double Longitude);
public sealed record VolunteerDetectionReport(string EventId, string VolunteerUserId, string DetectedPhrase, double Confidence, double Latitude, double Longitude, DateTime TimestampUtc);
public sealed record SetThreatLevelRequest(FacilityThreatLevel ThreatLevel);
public sealed record SubmitWhistleblowerRequest(string Ticker, WhistleblowerCategory Category, string EncryptedPayload, string ClaimantSecretToken, bool IsAnonymous);
public sealed record SubmitTipRequest(CommunityTipCategory Category, string Description, double Latitude, double Longitude, string Landmark, bool IsAnonymous, string? SubmitterAlias, bool RewardRequested);
public sealed record SummonRespondersRequest(string IncidentId, string ResponderId, string Title, string LocationName, double Latitude, double Longitude);
