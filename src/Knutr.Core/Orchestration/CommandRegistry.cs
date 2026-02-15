namespace Knutr.Core.Orchestration;

using System.Collections.Concurrent;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;

public sealed class CommandRegistry : ICommandRegistry, ICommandBuilder
{
    private readonly ConcurrentDictionary<string, Func<CommandContext, Task<PluginResult>>> _slash = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<MessageContext, Task<PluginResult>>> _message = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterSlash(string command, Func<CommandContext, Task<PluginResult>> handler)
        => _slash[Normalize(command)] = handler;

    public void RegisterMessage(string trigger, string[]? aliases, Func<MessageContext, Task<PluginResult>> handler)
    {
        _message[Normalize(trigger)] = handler;
        if (aliases != null)
            foreach (var a in aliases) _message[Normalize(a)] = handler;
    }

    public bool TryMatch(CommandContext ctx, out Func<CommandContext, Task<PluginResult>>? handler)
        => _slash.TryGetValue(Normalize(ctx.Command), out handler);

    public bool TryMatch(MessageContext ctx, out Func<MessageContext, Task<PluginResult>>? handler)
    {
        if (string.IsNullOrEmpty(ctx.Text)) { handler = null; return false; }

        // very simple impl: exact match on the first word
        var first = ctx.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return _message.TryGetValue(Normalize(first), out handler);
    }

    private static string Normalize(string s) => s.Trim().TrimStart('/').ToLowerInvariant();

    // ICommandBuilder (plugin-facing)
    public ICommandBuilder Slash(string command, Func<CommandContext, Task<PluginResult>> handler)
    {
        RegisterSlash(command, handler); return this;
    }

    public ICommandBuilder Message(string trigger, string[]? aliases, Func<MessageContext, Task<PluginResult>> handler)
    {
        RegisterMessage(trigger, aliases, handler); return this;
    }
}
