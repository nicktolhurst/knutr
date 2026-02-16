namespace Knutr.Abstractions.Plugins;

using Knutr.Abstractions.Events;

public interface ICommandBuilder
{
    ICommandBuilder Slash(string command, Func<CommandContext, Task<PluginResult>> handler);
    ICommandBuilder Message(string trigger, string[]? aliases, Func<MessageContext, Task<PluginResult>> handler);
}
