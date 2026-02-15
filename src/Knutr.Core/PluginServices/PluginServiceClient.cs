namespace Knutr.Core.PluginServices;

using System.Net.Http.Json;
using Knutr.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// HTTP client used by the core bot to call remote plugin services.
/// </summary>
public sealed class PluginServiceClient(
    IHttpClientFactory httpClientFactory,
    IOptions<PluginServiceOptions> options,
    ILogger<PluginServiceClient> logger)
{
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
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Failed to fetch manifest from \"{BaseUrl}\": {Reason}",
                baseUrl, ex.InnerException?.Message ?? ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch manifest from \"{BaseUrl}\"", baseUrl);
            return null;
        }
    }

    /// <summary>
    /// Execute a command on a remote plugin service.
    /// </summary>
    public async Task<PluginExecuteResponse> ExecuteAsync(PluginServiceEntry service, PluginExecuteRequest request, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("knutr-plugin-services");

        try
        {
            var target = request.Subcommand is { Length: > 0 }
                ? $"{request.Command} {request.Subcommand}"
                : request.Command;
            logger.LogInformation("Dispatching {Command} to remote service {Service} at {Url}",
                target, service.ServiceName, service.BaseUrl);

            var response = await client.PostAsJsonAsync($"{service.BaseUrl}/execute", request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PluginExecuteResponse>(ct);
            if (result is null)
            {
                logger.LogWarning("Null response from plugin service {Service}", service.ServiceName);
                return PluginExecuteResponse.Fail($"Null response from {service.ServiceName}");
            }

            return result;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute on plugin service {Service}", service.ServiceName);
            return PluginExecuteResponse.Fail($"Plugin service '{service.ServiceName}' error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send a scan request to a remote plugin service. Returns null if the service has nothing to say.
    /// </summary>
    public async Task<PluginExecuteResponse?> ScanAsync(PluginServiceEntry service, PluginScanRequest request, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("knutr-plugin-services");

        try
        {
            var response = await client.PostAsJsonAsync($"{service.BaseUrl}/scan", request, ct);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;
            return await response.Content.ReadFromJsonAsync<PluginExecuteResponse?>(ct);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to scan plugin service {Service}", service.ServiceName);
            return null;
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
