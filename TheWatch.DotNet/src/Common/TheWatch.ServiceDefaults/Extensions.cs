using Microsoft.AspNetCore.Builder;
using OpenTelemetry;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TheWatch.ServiceDefaults.FailSecure;
using TheWatch.ServiceDefaults.Mtls;
using TheWatch.ServiceDefaults.Resilience.DependencyInjection;
using TheWatch.ServiceDefaults.Completion.DependencyInjection;

namespace TheWatch.ServiceDefaults;

public static partial class Extensions
{
    public static HostApplicationBuilder AddServiceDefaults(this HostApplicationBuilder builder)
    {
        AddServiceDefaults((IHostApplicationBuilder)builder);
        return builder;
    }

    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        AddServiceDefaults((IHostApplicationBuilder)builder);
        builder.WebHost.ConfigureMtlsServerAuthentication(builder.Configuration);
        return builder;
    }

    public static IHostBuilder AddServiceDefaults(this IHostBuilder builder)
    {
        builder.ConfigureServices((context, services) =>
        {
            services.AddTheWatchGeneratedServiceDefaults();
            services.AddFailSecure(context.Configuration);
            services.AddMutualTls(context.Configuration);
            services.AddTheWatchResilience();
            services.AddTheWatchOperationalDefaults();
            services.AddServiceDiscovery();
            services.ConfigureHttpClientDefaults(http =>
            {
                http.AddStandardResilienceHandler();
                http.AddServiceDiscovery();
            });
            services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation())
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation());
        });
        return builder;
    }

    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.Services.AddTheWatchGeneratedServiceDefaults();
        builder.Services.AddFailSecure(builder.Configuration);
        builder.Services.AddMutualTls(builder.Configuration);
        builder.Services.AddTheWatchResilience();
        builder.Services.AddTheWatchOperationalDefaults();
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseTheWatchCorrelation();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });
        return app;
    }
}
