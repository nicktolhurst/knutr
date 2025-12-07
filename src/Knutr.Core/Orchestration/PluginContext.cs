namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;

/// <summary>
/// Implementation of IPluginContext that bundles command and hook builders.
/// </summary>
public sealed class PluginContext : IPluginContext
{
    public PluginContext(ICommandBuilder commands, IHookBuilder hooks)
    {
        Commands = commands;
        Hooks = hooks;
    }

    public ICommandBuilder Commands { get; }
    public IHookBuilder Hooks { get; }
}
