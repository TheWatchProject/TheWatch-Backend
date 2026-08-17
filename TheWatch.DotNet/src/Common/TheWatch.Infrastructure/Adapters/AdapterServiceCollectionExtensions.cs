using System;
using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;
using TheWatch.Infrastructure.Adapters.Cloud.Aws;
using TheWatch.Infrastructure.Adapters.Cloud.Azure;
using TheWatch.Infrastructure.Adapters.Cloud.Gcp;
using TheWatch.Infrastructure.Adapters.Cloud.R2;
using TheWatch.Infrastructure.Adapters.Messaging;
using TheWatch.Infrastructure.Adapters.Notifications;
using TheWatch.Infrastructure.Adapters.Persistence;
using TheWatch.Infrastructure.Adapters.Scheduling;
using TheWatch.Infrastructure.Adapters.SmartHome.Alexa;
using TheWatch.Infrastructure.Adapters.SmartHome.GoogleHome;
using TheWatch.Infrastructure.Adapters.SmartHome.Ring;
using TheWatch.Infrastructure.Adapters.Telemetry;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Messaging;
using TheWatch.Core.Messaging.DependencyInjection;
using TheWatch.Core.Messaging.Reliability;
using TheWatch.Infrastructure.Data;
using TheWatch.Infrastructure.Services;
using TheWatch.Contracts.Caching;
using TheWatch.Infrastructure.MultiTenancy;
using TheWatch.Core.Notifications.DependencyInjection;
using TheWatch.Infrastructure.Scheduling.DependencyInjection;
using TheWatch.Infrastructure.Workflows.DependencyInjection;
using TheWatch.Infrastructure.GeneratedAdapters.DependencyInjection;
using TheWatch.Infrastructure.ProductProviderAdapters.DependencyInjection;

namespace TheWatch.Infrastructure.Adapters;

public static class AdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default Dapr messaging, persistence, scheduling, notification, and telemetry adapters.
    /// </summary>
    public static IServiceCollection AddTheWatchAdapters(this IServiceCollection services)
    {
        services.AddTheWatchMessaging();
        services.AddTheWatchCompletionAdapters();
        services.AddTheWatchProductProviderAdapters();

        // Pluggable Adapters mapped to Application Ports
        services.AddSingleton<DaprPubSubAdapter>();
        services.AddSingleton<IMessageBusPort>(provider => provider.GetRequiredService<DaprPubSubAdapter>());
        services.AddSingleton<IMessageBus>(provider => provider.GetRequiredService<DaprPubSubAdapter>());
        services.AddTheWatchSchedulingRuntime();
        services.AddTheWatchWorkflowPersistence();
        services.AddSingleton<ISchedulerPort, InMemorySchedulerAdapter>();
        services.AddTheWatchNotifications();
        services.AddSingleton<UnifiedPushNotificationAdapter>();
        services.AddSingleton<INotificationPort>(provider => provider.GetRequiredService<UnifiedPushNotificationAdapter>());
        services.AddSingleton<ITelemetryPort, SensorTelemetryAdapter>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IOutboxPublisher, OutboxPublisher>();
        services.AddScoped<IOutboxStore, EfOutboxStoreAdapter>();
        services.AddSingleton<RedisDistributedCacheAdapter>();
        services.AddSingleton<ICacheStore>(provider => provider.GetRequiredService<RedisDistributedCacheAdapter>());
        services.AddHttpContextAccessor();
        services.AddScoped<HttpTenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<HttpTenantContext>());
        services.AddScoped<TheWatch.Contracts.MultiTenancy.ITenantContext>(provider => provider.GetRequiredService<HttpTenantContext>());

        return services;
    }

    /// <summary>
    /// Registers Azure Service Bus and EF-backed outbox adapters over the generated messaging contracts.
    /// </summary>
    public static IServiceCollection AddTheWatchAzureMessagingAdapters(this IServiceCollection services)
    {
        services.AddTheWatchMessaging();
        services.AddTheWatchCompletionAdapters();
        services.AddTheWatchProductProviderAdapters();
        services.AddSingleton<AzureServiceBusAdapter>();
        services.AddSingleton<IMessageBusPort>(provider => provider.GetRequiredService<AzureServiceBusAdapter>());
        services.AddSingleton<IMessageBus>(provider => provider.GetRequiredService<AzureServiceBusAdapter>());
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IOutboxPublisher, OutboxPublisher>();
        services.AddScoped<IOutboxStore, EfOutboxStoreAdapter>();
        services.AddTheWatchSchedulingRuntime();
        services.AddTheWatchWorkflowPersistence();
        services.AddSingleton<ISchedulerPort, InMemorySchedulerAdapter>();
        services.AddTheWatchNotifications();
        services.AddSingleton<UnifiedPushNotificationAdapter>();
        services.AddSingleton<INotificationPort>(provider => provider.GetRequiredService<UnifiedPushNotificationAdapter>());
        return services;
    }

    /// <summary>
    /// Registers the enterprise data adapters: Azure Cosmos DB, Microsoft SQL Server, PostgreSQL, and Firebase.
    /// </summary>
    public static IServiceCollection AddTheWatchDataAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        var cosmosConnectionString = configuration.GetConnectionString("CosmosDb");
        if (!string.IsNullOrEmpty(cosmosConnectionString))
        {
            services.AddSingleton(sp => new CosmosClient(cosmosConnectionString, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase }
            }));
            services.AddScoped(typeof(ICosmosDbPort<>), typeof(CosmosDbDatabaseAdapter<>));
        }

        services.AddScoped(typeof(ISqlServerPort<>), typeof(SqlServerDatabaseAdapter<,>));
        services.AddScoped(typeof(IPostgreSqlPort<>), typeof(PostgreSqlDatabaseAdapter<,>));

        var firebaseProjectId = configuration["Firebase:ProjectId"] ?? "thewatch-prod";
        var firebaseDbUrl = configuration["Firebase:DatabaseUrl"] ?? "https://thewatch-prod-default-rtdb.firebaseio.com";
        services.AddHttpClient<IFirebasePort, FirebasePushAndDataStoreAdapter>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IFirebasePort>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<FirebasePushAndDataStoreAdapter>>();
            return new FirebasePushAndDataStoreAdapter(httpClientFactory.CreateClient(nameof(IFirebasePort)), firebaseProjectId, firebaseDbUrl, logger);
        });

        services.AddScoped(typeof(IDatabasePort<>), typeof(GenericEfCoreAdapter<,>));
        return services;
    }

    /// <summary>
    /// Registers multi-cloud adapters across Azure, Google Cloud (GCP), Amazon Web Services (AWS), and Cloudflare R2.
    /// </summary>
    public static IServiceCollection AddTheWatchMultiCloudAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Azure Cloud Unified Adapter
        services.AddScoped<AzureCloudUnifiedAdapter>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AzureCloudUnifiedAdapter>>();
            var blobConn = configuration.GetConnectionString("AzureBlobStorage");
            var sbConn = configuration.GetConnectionString("AzureServiceBus");
            return new AzureCloudUnifiedAdapter(logger, blobConn, sbConn);
        });

        // 2. Google Cloud Unified Adapter
        services.AddHttpClient<GoogleCloudUnifiedAdapter>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<GoogleCloudUnifiedAdapter>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<GoogleCloudUnifiedAdapter>>();
            var gcpProject = configuration["GoogleCloud:ProjectId"] ?? "thewatch-gcp-prod";
            return new GoogleCloudUnifiedAdapter(factory.CreateClient(nameof(GoogleCloudUnifiedAdapter)), gcpProject, logger);
        });

        // 3. AWS Unified Adapter
        services.AddHttpClient<AwsCloudUnifiedAdapter>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<AwsCloudUnifiedAdapter>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<AwsCloudUnifiedAdapter>>();
            var region = configuration["AWS:Region"] ?? "us-east-1";
            return new AwsCloudUnifiedAdapter(factory.CreateClient(nameof(AwsCloudUnifiedAdapter)), region, logger);
        });

        // 4. Default Multi-Cloud Port Bindings
        services.AddScoped<ICloudStoragePort>(sp => sp.GetRequiredService<AzureCloudUnifiedAdapter>());
        services.AddScoped<ICloudSecretsPort>(sp => sp.GetRequiredService<AzureCloudUnifiedAdapter>());
        services.AddScoped<ICloudEventMeshPort>(sp => sp.GetRequiredService<AzureCloudUnifiedAdapter>());

        return services;
    }

    /// <summary>
    /// Registers Smart Home and IoT Security Adapters: Google Home / Nest, Ring Doorbell, and Amazon Alexa.
    /// </summary>
    public static IServiceCollection AddTheWatchSmartHomeAndWearableAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Google Home / Nest Device Adapter
        services.AddHttpClient<GoogleHomeSmartDeviceAdapter>();
        services.AddScoped<GoogleHomeSmartDeviceAdapter>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<GoogleHomeSmartDeviceAdapter>>();
            var projectId = configuration["GoogleHome:ProjectId"] ?? "thewatch-nest-prod";
            return new GoogleHomeSmartDeviceAdapter(factory.CreateClient(nameof(GoogleHomeSmartDeviceAdapter)), projectId, logger);
        });

        // 2. Ring Doorbell & Alarm Security Adapter
        services.AddHttpClient<RingDoorbellSecurityAdapter>();
        services.AddScoped<RingDoorbellSecurityAdapter>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<RingDoorbellSecurityAdapter>>();
            var apiToken = configuration["Ring:ApiToken"] ?? "thewatch-ring-token";
            return new RingDoorbellSecurityAdapter(factory.CreateClient(nameof(RingDoorbellSecurityAdapter)), apiToken, logger);
        });

        // 3. Amazon Alexa & Echo Smart Home Skills Adapter
        services.AddHttpClient<AmazonAlexaSmartDeviceAdapter>();
        services.AddScoped<AmazonAlexaSmartDeviceAdapter>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<AmazonAlexaSmartDeviceAdapter>>();
            var skillId = configuration["Alexa:SkillId"] ?? "amzn1.ask.skill.thewatch";
            return new AmazonAlexaSmartDeviceAdapter(factory.CreateClient(nameof(AmazonAlexaSmartDeviceAdapter)), skillId, logger);
        });

        // 4. Default Smart Home Port Mapping (Google Home + Ring + Alexa)
        services.AddScoped<ISmartHomeIoTDevicePort>(sp => sp.GetRequiredService<GoogleHomeSmartDeviceAdapter>());

        return services;
    }
}
