namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;

public sealed class CommandRouter(ICommandRegistry registry)
{
    public bool TryRoute(CommandContext ctx, out Func<CommandContext, Task<PluginResult>>? handler, out string? subcommand)
    {
        subcommand = ExtractSubcommand(ctx.RawText);
        return registry.TryMatch(ctx, out handler);
    }

    public bool TryRoute(MessageContext ctx, out Func<MessageContext, Task<PluginResult>>? handler, out string? subcommand)
    {
        subcommand = ExtractSubcommand(ctx.Text);
        return registry.TryMatch(ctx, out handler);
    }

    private static string? ExtractSubcommand(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1].ToLowerInvariant() : null;
    }
}
