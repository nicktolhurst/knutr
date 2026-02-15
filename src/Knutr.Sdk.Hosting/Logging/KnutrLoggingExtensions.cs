namespace Knutr.Sdk.Hosting.Logging;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;


/// <summary>
/// One-liner Serilog setup shared by the core host and every plugin service.
/// Produces a standardized, colorized console format with per-service coloring
/// so interleaved kubectl logs are easy to scan.
/// </summary>
public static class KnutrLoggingExtensions
{
    /// <summary>
    /// Configures Serilog with the Knutr console formatter for the given service.
    /// Sets up a bootstrap logger, suppresses noisy ASP.NET request logs, and
    /// still respects any Serilog overrides in appsettings.json.
    /// </summary>
    public static WebApplicationBuilder AddKnutrLogging(this WebApplicationBuilder builder, string serviceName)
    {
        var formatter = new KnutrConsoleFormatter(serviceName);

        builder.Logging.ClearProviders();

        builder.Host.UseSerilog((ctx, cfg) => cfg
            // .ReadFrom.Configuration(ctx.Configuration)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Fatal)
            .MinimumLevel.Override("System", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(formatter));

        return builder;
    }
}
