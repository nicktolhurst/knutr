namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;

public sealed class CommandRouter
{
    private readonly ICommandRegistry _registry;

    public CommandRouter(ICommandRegistry registry) => _registry = registry;

    public bool TryRoute(CommandContext ctx, out Func<CommandContext, Task<PluginResult>>? handler)
        => _registry.TryMatch(ctx, out handler);

    public bool TryRoute(MessageContext ctx, out Func<MessageContext, Task<PluginResult>>? handler)
        => _registry.TryMatch(ctx, out handler);
}
