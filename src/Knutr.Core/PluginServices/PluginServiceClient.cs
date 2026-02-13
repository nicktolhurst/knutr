namespace Knutr.Core.PluginServices;

using System.Diagnostics;
using System.Net.Http.Json;
using Knutr.Sdk;
using Knutr.Core.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// HTTP client used by the core bot to call remote plugin services.
/// </summary>
public sealed class PluginServiceClient(
    IHttpClientFactory httpClientFactory,
    IOptions<PluginServiceOptions> options,
    CoreMetrics metrics,
    ILogger<PluginServiceClient> logger)
{
    private static readonly ActivitySource Activity = new("Knutr.Core.PluginServices");

    /// <summary>
    /// Fetch the manifest from a plugin service.
    /// </summary>
    public async Task<PluginManifest?> FetchManifestAsync(string baseUrl, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("knutr-plugin-services");
        try
        {
            var response = await client.GetAsync($"{baseUrl}/manifest", ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PluginManifest>(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch manifest from {BaseUrl}", baseUrl);
            return null;
        }
    }

    /// <summary>
    /// Execute a command on a remote plugin service.
    /// </summary>
    public async Task<PluginExecuteResponse> ExecuteAsync(PluginServiceEntry service, PluginExecuteRequest request, CancellationToken ct = default)
    {
        using var act = Activity.StartActivity("plugin_service_call");
        act?.SetTag("service", service.ServiceName);
        act?.SetTag("command", request.Command);
        act?.SetTag("subcommand", request.Subcommand);

        var sw = Stopwatch.StartNew();
        var client = httpClientFactory.CreateClient("knutr-plugin-services");

        try
        {
            // Propagate trace context via request header (W3C traceparent) and request body
            request.TraceId = act?.TraceId.ToString();

            logger.LogInformation("Dispatching {Command}/{Subcommand} to remote service {Service} at {Url}",
                request.Command, request.Subcommand, service.ServiceName, service.BaseUrl);

            var response = await client.PostAsJsonAsync($"{service.BaseUrl}/execute", request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PluginExecuteResponse>(ct);
            if (result is null)
            {
                logger.LogWarning("Null response from plugin service {Service}", service.ServiceName);
                return PluginExecuteResponse.Fail($"Null response from {service.ServiceName}");
            }

            act?.SetTag("success", result.Success);
            return result;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            act?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Failed to execute on plugin service {Service}", service.ServiceName);
            metrics.RecordError("plugin_service", service.ServiceName, ex.GetType().Name);
            return PluginExecuteResponse.Fail($"Plugin service '{service.ServiceName}' error: {ex.Message}");
        }
        finally
        {
            logger.LogDebug("Plugin service call to {Service} completed in {ElapsedMs}ms", service.ServiceName, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Resolve a service name to its base URL.
    /// </summary>
    public string ResolveServiceUrl(string serviceName)
    {
        var opts = options.Value;

        if (opts.Endpoints.TryGetValue(serviceName, out var explicitUrl))
            return explicitUrl.TrimEnd('/');

        return $"http://knutr-plugin-{serviceName}.{opts.Namespace}.svc.cluster.local";
    }
}
