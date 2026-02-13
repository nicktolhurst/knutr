namespace Knutr.Sdk.Hosting;

using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// HTTP client for calling other plugin services. Resolves service names to URLs
/// using K8s DNS convention or explicit configuration overrides.
/// </summary>
internal sealed class HttpPluginServiceClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HttpPluginServiceClient> logger) : IPluginServiceClient
{
    public async Task<PluginExecuteResponse> CallAsync(string serviceName, PluginExecuteRequest request, CancellationToken ct = default)
    {
        var baseUrl = ResolveServiceUrl(serviceName);
        logger.LogInformation("Calling plugin service {ServiceName} at {BaseUrl}", serviceName, baseUrl);

        var client = httpClientFactory.CreateClient("knutr-plugin-services");

        try
        {
            var response = await client.PostAsJsonAsync($"{baseUrl}/execute", request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PluginExecuteResponse>(ct);
            return result ?? PluginExecuteResponse.Fail($"Null response from {serviceName}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call plugin service {ServiceName}", serviceName);
            return PluginExecuteResponse.Fail($"Failed to reach plugin service '{serviceName}': {ex.Message}");
        }
    }

    private string ResolveServiceUrl(string serviceName)
    {
        // Check for explicit URL override in config: PluginServices:Endpoints:channel-export = http://localhost:5100
        var configuredUrl = configuration[$"PluginServices:Endpoints:{serviceName}"];
        if (!string.IsNullOrEmpty(configuredUrl))
            return configuredUrl.TrimEnd('/');

        // Default K8s DNS convention: http://knutr-plugin-{name}.{namespace}.svc.cluster.local
        var ns = configuration["PluginServices:Namespace"] ?? "knutr";
        return $"http://knutr-plugin-{serviceName}.{ns}.svc.cluster.local";
    }
}
