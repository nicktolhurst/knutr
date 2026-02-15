namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;

/// <summary>
/// Implementation of IPluginContext that bundles command, subcommand, and hook builders.
/// </summary>
public sealed class PluginContext(ICommandBuilder commands, ISubcommandBuilder subcommands, IHookBuilder hooks) : IPluginContext
{
    public ICommandBuilder Commands { get; } = commands;
    public ISubcommandBuilder Subcommands { get; } = subcommands;
    public IHookBuilder Hooks { get; } = hooks;
}
