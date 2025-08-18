namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;

public interface ICommandRegistry
{
    void RegisterSlash(string command, Func<CommandContext, Task<PluginResult>> handler);
    void RegisterMessage(string trigger, string[]? aliases, Func<MessageContext, Task<PluginResult>> handler);

    bool TryMatch(CommandContext ctx, out Func<CommandContext, Task<PluginResult>>? handler);
    bool TryMatch(MessageContext ctx, out Func<MessageContext, Task<PluginResult>>? handler);
}
