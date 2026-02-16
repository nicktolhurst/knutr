namespace Knutr.Abstractions.Plugins;

public interface IBotPlugin
{
    string Name { get; }

    /// <summary>
    /// Configures the plugin with access to commands and hooks.
    /// Override this method to register both commands and lifecycle hooks.
    /// </summary>
    void Configure(IPluginContext context)
    {
        // Default implementation calls legacy method for backwards compatibility
        Configure(context.Commands);
    }

    /// <summary>
    /// Legacy configuration method for commands only.
    /// Prefer overriding Configure(IPluginContext) instead.
    /// </summary>
    void Configure(ICommandBuilder commands) { }
}
