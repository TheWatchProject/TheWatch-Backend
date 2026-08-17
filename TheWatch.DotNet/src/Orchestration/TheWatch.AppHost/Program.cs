var builder = DistributedApplication.CreateBuilder(args);

// ============================================
// Distributed Infrastructure Resources
// ============================================
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("thewatchdb");

var redis = builder.AddRedis("redis")
    .WithDataVolume();

var messaging = builder.AddRabbitMQ("messaging")
    .WithDataVolume();

// ============================================
// Microservices Squad
// ============================================
var authService = builder.AddProject<Projects.AuthService>("auth-service")
    .WithReference(postgres)
    .WithReference(redis);

var locationService = builder.AddProject<Projects.LocationService>("location-service")
    .WithReference(redis)
    .WithReference(messaging);

var notificationService = builder.AddProject<Projects.NotificationService>("notification-service")
    .WithReference(messaging)
    .WithReference(redis);

var incidentService = builder.AddProject<Projects.IncidentService>("incident-service")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(messaging)
    .WithReference(locationService)
    .WithReference(notificationService);

var dispatchService = builder.AddProject<Projects.DispatchService>("dispatch-service")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(messaging)
    .WithReference(incidentService)
    .WithReference(locationService);

var triageService = builder.AddProject<Projects.TriageService>("triage-service")
    .WithReference(messaging);

var auditService = builder.AddProject<Projects.AuditService>("audit-service")
    .WithReference(postgres);

var aiInferenceService = builder.AddProject<Projects.AiInferenceService>("ai-inference-service")
    .WithReference(redis);

var meshGatewayService = builder.AddProject("mesh-gateway-service", "../../../../TheWatch.Microservices/src/MeshGatewayService/MeshGatewayService.csproj")
    .WithReference(messaging)
    .WithReference(redis);

// ============================================
// Core Emergency Response & Serverless Functions
// ============================================
var emergencyService = builder.AddProject<Projects.TheWatch_EmergencyService>("emergency-service")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(messaging)
    .WithReference(incidentService)
    .WithReference(dispatchService);

var functions = builder.AddProject<Projects.TheWatch_Functions>("serverless-functions")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(messaging)
    .WithReference(emergencyService);

// ============================================
// Mobile Backend-For-Frontend & Web Gateways
// ============================================
var mobileBff = builder.AddProject<Projects.TheWatch_MobileBff>("mobile-bff")
    .WithReference(emergencyService)
    .WithReference(incidentService)
    .WithReference(locationService)
    .WithReference(redis);

var adminWeb = builder.AddProject<Projects.TheWatch_Web_Admin>("admin-hq-portal")
    .WithReference(emergencyService)
    .WithReference(incidentService)
    .WithReference(dispatchService)
    .WithReference(functions);

builder.AddProject<Projects.TheWatch_ApiGateway>("api-gateway")
    .WithReference(emergencyService)
    .WithReference(mobileBff)
    .WithReference(adminWeb)
    .WithReference(functions);

builder.Build().Run();