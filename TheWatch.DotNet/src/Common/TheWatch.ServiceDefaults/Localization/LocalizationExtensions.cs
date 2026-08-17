using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TheWatch.ServiceDefaults.Localization;

/// <summary>
/// Extension methods for configuring globalization and request localization middleware.
/// </summary>
public static class LocalizationExtensions
{
    /// <summary>
    /// Configures supported cultures (en, es, fr, de, ja, ar, zh) and request culture providers.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder instance.</returns>
    public static IHostApplicationBuilder AddTheWatchLocalization(this IHostApplicationBuilder builder)
    {
        builder.Services.AddLocalization();

        var supportedCultures = new[]
        {
            new CultureInfo("en-US"),
            new CultureInfo("es-ES"),
            new CultureInfo("fr-FR"),
            new CultureInfo("de-DE"),
            new CultureInfo("ja-JP"),
            new CultureInfo("ar-SA"),
            new CultureInfo("zh-CN")
        };

        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en-US");
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
            options.ApplyCurrentCultureToResponseHeaders = true;
        });

        return builder;
    }

    /// <summary>
    /// Wires the RequestLocalizationMiddleware into the HTTP pipeline.
    /// </summary>
    /// <param name="app">The web application instance.</param>
    /// <returns>The application instance.</returns>
    public static WebApplication UseTheWatchLocalization(this WebApplication app)
    {
        app.UseRequestLocalization();
        return app;
    }
}