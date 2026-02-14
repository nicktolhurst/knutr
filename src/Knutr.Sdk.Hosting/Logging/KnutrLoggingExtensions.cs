namespace Knutr.Sdk.Hosting.Logging;

using Microsoft.AspNetCore.Builder;
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

        // Bootstrap logger for the brief window before DI is ready.
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(formatter)
            .CreateBootstrapLogger();

        builder.Host.UseSerilog((ctx, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(formatter));

        return builder;
    }
}
