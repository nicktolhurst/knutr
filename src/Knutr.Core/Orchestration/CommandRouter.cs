namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;

public sealed class CommandRouter(ICommandRegistry registry)
{
    public bool TryRoute(CommandContext ctx, out Func<CommandContext, Task<PluginResult>>? handler)
        => registry.TryMatch(ctx, out handler);

    public bool TryRoute(MessageContext ctx, out Func<MessageContext, Task<PluginResult>>? handler)
        => registry.TryMatch(ctx, out handler);
}
