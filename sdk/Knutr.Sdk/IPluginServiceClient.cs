namespace Knutr.Sdk;

/// <summary>
/// Client interface for calling other plugin services from within a plugin service.
/// Enables service chaining (e.g., post-mortem service calls channel-export service).
/// Injected via DI in plugin services that use Knutr.Sdk.Hosting.
/// </summary>
public interface IPluginServiceClient
{
    /// <summary>
    /// Call another plugin service by name.
    /// </summary>
    /// <param name="serviceName">
    /// The logical service name (e.g., "channel-export").
    /// Resolved to a K8s service URL via convention: http://knutr-plugin-{serviceName}.
    /// </param>
    /// <param name="request">The execution request.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PluginExecuteResponse> CallAsync(string serviceName, PluginExecuteRequest request, CancellationToken ct = default);
}
