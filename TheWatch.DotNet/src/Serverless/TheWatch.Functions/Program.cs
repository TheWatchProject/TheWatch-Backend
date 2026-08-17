using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using TheWatch.Core.Logging;
using TheWatch.Infrastructure.Logging;
using TheWatch.Core.Interfaces;
using TheWatch.Infrastructure.Data;
using TheWatch.Infrastructure.Data.Repositories;
using TheWatch.Infrastructure.Services;
using TheWatch.Infrastructure.Adapters;
using TheWatch.Infrastructure.Persistence.Generated;

var builder = FunctionsApplication.CreateBuilder(args);

// ============================================
// Advanced Logging (PII-safe, structured)
// ============================================

builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();

    static PiiRedactionOptions GetPiiRedactionOptions(IConfiguration configuration)
    {
        return new PiiRedactionOptions
        {
            Enabled = configuration.GetValue("Logging:Redaction:Enabled", true),
            RedactStructuredState = configuration.GetValue("Logging:Redaction:RedactStructuredState", true),
            RedactScopes = configuration.GetValue("Logging:Redaction:RedactScopes", true),
            SanitizeExceptions = configuration.GetValue("Logging:Redaction:SanitizeExceptions", true),
            MaxStringLength = configuration.GetValue("Logging:Redaction:MaxStringLength", 256)
        };
    }

    var redactionOptions = GetPiiRedactionOptions(builder.Configuration);

    var innerLoggerFactory = LoggerFactory.Create(lb =>
    {
        lb.AddConfiguration(builder.Configuration.GetSection("Logging"));
        lb.Configure(o =>
        {
            o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId;
        });

        lb.AddJsonConsole(o =>
        {
            o.IncludeScopes = true;
            o.TimestampFormat = "O";
            o.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
        });
    });

    logging.AddProvider(new PiiRedactionLoggerProvider(innerLoggerFactory, redactionOptions));
});

builder.Services.AddApplicationInsightsTelemetryWorkerService();

// Get configuration
var configuration = builder.Configuration;

// ============================================
// Database Configuration
// ============================================

builder.Services.AddDbContext<WatchDbContext>(options =>
{
    var connectionString = configuration["SqlConnectionString"]
        ?? throw new InvalidOperationException("SqlConnectionString not configured");
    options.UseSqlServer(connectionString);
});
builder.Services.AddTheWatchEfPersistence();

// ============================================
// Azure Service Clients
// ============================================

// Cosmos DB Client (for real-time location data)
builder.Services.AddSingleton(sp =>
{
    var connectionString = configuration["AzureCosmosConnectionString"];
    if (string.IsNullOrEmpty(connectionString))
    {
        // Return null for local dev without Cosmos
        return null!;
    }
    return new CosmosClient(connectionString);
});

// Service Bus Client (for event queuing)
builder.Services.AddSingleton(sp =>
{
    var connectionString = configuration["AzureServiceBusConnectionString"];
    if (string.IsNullOrEmpty(connectionString))
    {
        return null!;
    }
    return new ServiceBusClient(connectionString);
});

// Blob Storage Client (for evidence, photos, videos)
builder.Services.AddSingleton(sp =>
{
    var connectionString = configuration["AzureStorageConnectionString"]
        ?? "UseDevelopmentStorage=true";
    return new BlobServiceClient(connectionString);
});

builder.Services.AddTheWatchAzureMessagingAdapters();

// ============================================
// Core Services
// ============================================

// Cryptography Service
builder.Services.AddSingleton<ICryptographyService>(sp =>
{
    var encryptionKey = configuration["CryptographySettings:EncryptionKeyBase64"]
        ?? throw new InvalidOperationException("Encryption key not configured");
    return new CryptographyService(encryptionKey);
});

// JWT Service
builder.Services.AddSingleton<IJwtService>(sp =>
{
    var issuer = configuration["JwtSettings:Issuer"]
        ?? throw new InvalidOperationException("JWT Issuer not configured");
    var audience = configuration["JwtSettings:Audience"]
        ?? throw new InvalidOperationException("JWT Audience not configured");
    var privateKey = configuration["JwtSettings:RsaPrivateKeyPem"]
        ?? throw new InvalidOperationException("RSA private key not configured");
    var publicKey = configuration["JwtSettings:RsaPublicKeyPem"]
        ?? throw new InvalidOperationException("RSA public key not configured");

    return new JwtService(privateKey, publicKey, issuer, audience);
});

// Notification Service
builder.Services.AddScoped<INotificationService, NotificationService>();

// Realtime Service (SignalR)
builder.Services.AddScoped<IRealtimeService, RealtimeService>();

// HQ Broadcast Service
builder.Services.AddScoped<IHqBroadcastService, HqBroadcastService>();

// Disaster Zone Service
builder.Services.AddScoped<IDisasterZoneService, DisasterZoneService>();

// Dispatch Service
builder.Services.AddScoped<IDispatchService, DispatchService>();

// Location Service (Cosmos DB)
builder.Services.AddScoped<ILocationService, CosmosLocationService>();

// Blob Storage Service
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();

// Evidence Storage Service
builder.Services.AddScoped<EvidenceStorageService>();

// Responder Schedule Service
builder.Services.AddScoped<IResponderScheduleService, ResponderScheduleService>();

// Tactical Pathfinding, Crowd Safety & Whistleblower Engines
builder.Services.AddSingleton<TheWatch.Geospatial.Db.IConstrainedPathfinder, TheWatch.Geospatial.Db.ConstrainedAStarPathfinder>();
builder.Services.AddSingleton<TheWatch.Geospatial.Db.IVolunteerCrowdMonitoringEngine, TheWatch.Geospatial.Db.VolunteerCrowdMonitoringEngine>();
builder.Services.AddSingleton<TheWatch.Geospatial.Db.IWhistleblowerAndTipsEngine, TheWatch.Geospatial.Db.WhistleblowerAndTipsEngine>();

// ============================================
// Security & Compliance Services
// ============================================

// Duress PIN Service (critical security feature)
builder.Services.AddScoped<IDuressPinService, DuressPinService>();

// Step-Up Authentication Service
builder.Services.AddScoped<IStepUpAuthService, StepUpAuthService>();

// GDPR Compliance Service
builder.Services.AddScoped<IComplianceService, ComplianceService>();

// ============================================
// Repositories
// ============================================

builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IIncidentRepository, IncidentRepository>();
builder.Services.AddScoped<IEvidenceRepository, EvidenceRepository>();
builder.Services.AddScoped<IEvacuationRepository, EvacuationRepository>();
builder.Services.AddScoped<IDisasterZoneRepository, DisasterZoneRepository>();
builder.Services.AddScoped<ILegalAgreementRepository, LegalAgreementRepository>();
builder.Services.AddScoped<IResponderOnboardingRepository, ResponderOnboardingRepository>();
builder.Services.AddScoped<IAdminAuditRepository, AdminAuditRepository>();
builder.Services.AddScoped<ITriggerPhraseRepository, TriggerPhraseRepository>();
builder.Services.AddScoped<IDuressPinRepository, DuressPinRepository>();
builder.Services.AddScoped<ISafetySettingsRepository, SafetySettingsRepository>();
builder.Services.AddScoped<ISummonerPhotoRepository, SummonerPhotoRepository>();
builder.Services.AddScoped<IResponderScheduleRepository, ResponderScheduleRepository>();

// ============================================
// Build and Run
// ============================================

builder.Build().Run();
