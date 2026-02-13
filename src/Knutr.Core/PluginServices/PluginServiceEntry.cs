namespace Knutr.Core.PluginServices;

using Knutr.Sdk;

/// <summary>
/// Represents a discovered remote plugin service with its manifest and endpoint.
/// </summary>
public sealed class PluginServiceEntry
{
    public required string ServiceName { get; init; }
    public required string BaseUrl { get; init; }
    public required PluginManifest Manifest { get; init; }
}
