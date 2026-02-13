namespace Knutr.Core.PluginServices;

/// <summary>
/// Configuration for remote plugin services.
/// Bound from the "PluginServices" configuration section.
/// </summary>
public sealed class PluginServiceOptions
{
    public const string SectionName = "PluginServices";

    /// <summary>
    /// The K8s namespace where plugin services are deployed.
    /// Used for DNS-based service discovery: knutr-plugin-{name}.{namespace}.svc.cluster.local
    /// </summary>
    public string Namespace { get; set; } = "knutr";

    /// <summary>
    /// Explicit service endpoint overrides. Key = service name, Value = base URL.
    /// Useful for local development or non-K8s environments.
    /// Example: { "channel-export": "http://localhost:5100" }
    /// </summary>
    public Dictionary<string, string> Endpoints { get; set; } = new();

    /// <summary>
    /// List of plugin services to discover at startup.
    /// Each entry is a service name that will be resolved via K8s DNS or Endpoints config.
    /// </summary>
    public List<string> Services { get; set; } = [];

    /// <summary>
    /// Timeout in seconds for calls to plugin services.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How often (in seconds) to re-discover plugin service manifests.
    /// 0 = only at startup.
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 0;
}
