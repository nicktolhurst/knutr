using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

namespace Knutr.Hosting.Extensions;

public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddKnutrObservability(this WebApplicationBuilder builder)
    {
        var lokiUrl = builder.Configuration.GetValue<string>("Observability:LokiUrl") ?? "http://localhost:3100";
        var serviceName = builder.Configuration.GetValue<string>("Observability:ServiceName") ?? "knutr";

        builder.Host.UseSerilog((ctx, services, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration)
               .MinimumLevel.Override("Knutr", LogEventLevel.Information)
               .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
               .Enrich.FromLogContext()
               .Enrich.WithProperty("Application", serviceName)
               .WriteTo.GrafanaLoki(
                   lokiUrl,
                   labels:
                   [
                       new LokiLabel { Key = "app", Value = serviceName },
                       new LokiLabel { Key = "env", Value = ctx.HostingEnvironment.EnvironmentName.ToLowerInvariant() }
                   ],
                   propertiesAsLabels: ["level", "SourceContext"]);
        });

        var otlp = builder.Configuration.GetValue<string>("OpenTelemetry:Tracing:OtlpEndpoint");
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(opts =>
                 {
                     opts.RecordException = true;
                 })
                 .AddHttpClientInstrumentation(opts =>
                 {
                     opts.RecordException = true;
                 })
                 .AddSource("Knutr.Core", "Knutr.Slack", "Knutr.NL", "Knutr.Plugins", "Knutr.Plugins.EnvironmentClaim", "Knutr.Infrastructure.Llm");

                if (!string.IsNullOrEmpty(otlp))
                {
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation();
                m.AddHttpClientInstrumentation();
                m.AddMeter("Knutr.Core", "Knutr.Plugins.EnvironmentClaim", "Knutr.Infrastructure.Llm");
                m.AddPrometheusExporter();
            });

        return builder;
    }
}
