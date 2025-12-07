namespace Knutr.Abstractions.Plugins;

using Knutr.Abstractions.Hooks;

/// <summary>
/// Provides access to command and hook registration for plugins.
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// Builder for registering slash commands and message patterns.
    /// </summary>
    ICommandBuilder Commands { get; }

    /// <summary>
    /// Builder for registering subcommands under /knutr.
    /// </summary>
    ISubcommandBuilder Subcommands { get; }

    /// <summary>
    /// Builder for registering lifecycle hooks.
    /// </summary>
    IHookBuilder Hooks { get; }
}
