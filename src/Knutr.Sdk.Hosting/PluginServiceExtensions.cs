namespace Knutr.Sdk.Hosting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class PluginServiceExtensions
{
    /// <summary>
    /// Registers a plugin handler and the infrastructure needed to run as a Knutr plugin service.
    /// </summary>
    /// <typeparam name="THandler">The plugin handler implementation.</typeparam>
    public static IServiceCollection AddKnutrPluginService<THandler>(this IServiceCollection services)
        where THandler : class, IPluginHandler
    {
        services.AddSingleton<IPluginHandler, THandler>();
        services.AddHealthChecks();

        // Register the inter-service HTTP client
        services.AddHttpClient("knutr-plugin-services", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        });
        services.AddSingleton<IPluginServiceClient, HttpPluginServiceClient>();

        return services;
    }

    /// <summary>
    /// Maps the standard Knutr plugin service endpoints: /health, /manifest, /execute, /scan.
    /// </summary>
    public static IEndpointRouteBuilder MapKnutrPluginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health");

        endpoints.MapGet("/manifest", (IPluginHandler handler) =>
        {
            return Results.Ok(handler.GetManifest());
        });

        endpoints.MapPost("/execute", async (PluginExecuteRequest request, IPluginHandler handler, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            try
            {
                var response = await handler.ExecuteAsync(request, ct);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                var logger = loggerFactory.CreateLogger("Knutr.Sdk.Hosting.Execute");
                logger.LogError(ex, "Plugin execution failed for {Command}/{Subcommand}", request.Command, request.Subcommand);
                return Results.Ok(PluginExecuteResponse.Fail($"Internal plugin error: {ex.Message}"));
            }
        });

        endpoints.MapPost("/scan", async (PluginScanRequest request, IPluginHandler handler, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            try
            {
                var response = await handler.ScanAsync(request, ct);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                var logger = loggerFactory.CreateLogger("Knutr.Sdk.Hosting.Scan");
                logger.LogError(ex, "Plugin scan failed");
                return Results.Ok(PluginExecuteResponse.Fail($"Internal plugin error: {ex.Message}"));
            }
        });

        return endpoints;
    }
}
