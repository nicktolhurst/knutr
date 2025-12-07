namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;

/// <summary>
/// Implementation of IPluginContext that bundles command, subcommand, and hook builders.
/// </summary>
public sealed class PluginContext : IPluginContext
{
    public PluginContext(ICommandBuilder commands, ISubcommandBuilder subcommands, IHookBuilder hooks)
    {
        Commands = commands;
        Subcommands = subcommands;
        Hooks = hooks;
    }

    public ICommandBuilder Commands { get; }
    public ISubcommandBuilder Subcommands { get; }
    public IHookBuilder Hooks { get; }
}
