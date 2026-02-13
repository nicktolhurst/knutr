namespace Knutr.Sdk;

/// <summary>
/// Implement this interface to handle commands in a plugin service.
/// The hosting framework will route requests to the appropriate handler.
/// </summary>
public interface IPluginHandler
{
    /// <summary>
    /// Returns the manifest declaring what this plugin handles.
    /// </summary>
    PluginManifest GetManifest();

    /// <summary>
    /// Execute a command or subcommand.
    /// </summary>
    Task<PluginExecuteResponse> ExecuteAsync(PluginExecuteRequest request, CancellationToken ct = default);
}
