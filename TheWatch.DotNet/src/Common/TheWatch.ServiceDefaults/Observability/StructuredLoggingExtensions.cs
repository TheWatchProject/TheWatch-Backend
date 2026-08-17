using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TheWatch.ServiceDefaults.Observability;

public static class StructuredLoggingExtensions
{
    public static IHostApplicationBuilder AddTheWatchStructuredLogging(this IHostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            options.UseUtcTimestamp = true;
        });

        return builder;
    }
}