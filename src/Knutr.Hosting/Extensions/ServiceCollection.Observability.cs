using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Knutr.Hosting.Extensions;

public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddKnutrObservability(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((ctx, services, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration)
               .MinimumLevel.Override("Knutr", LogEventLevel.Information)
               .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
               .Enrich.FromLogContext()
               .WriteTo.Console();
        });

        var otlp = builder.Configuration.GetValue<string>("OpenTelemetry:Tracing:OtlpEndpoint");
        builder.Services.AddOpenTelemetry()
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddSource("Knutr.Core","Knutr.Slack","Knutr.NL","Knutr.Plugins");

                if (!string.IsNullOrEmpty(otlp))
                {
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation();
                m.AddHttpClientInstrumentation();
                m.AddPrometheusExporter();
            });

        return builder;
    }
}
