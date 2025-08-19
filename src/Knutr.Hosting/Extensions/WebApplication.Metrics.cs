namespace Knutr.Hosting.Extensions;

public static class MetricsWebAppExtensions
{
    public static WebApplication UseBotPrometheus(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint(); // from OpenTelemetry.Exporter.Prometheus.AspNetCore
        return app;
    }
}
