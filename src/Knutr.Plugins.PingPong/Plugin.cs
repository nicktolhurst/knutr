namespace Knutr.Plugins.PingPong;

using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.Events;

public sealed class Plugin : IBotPlugin
{
    public string Name => "PingPong";

    // One allocation, reused.
    private static readonly PluginResult Pong = PluginResult.SkipNl(new Reply("pong"));

    public void Configure(ICommandBuilder commands)
    {
        commands
            .Slash("ping", HandlePing)
            .Message("ping", ["Ping", "PING"], HandlePing);
    }

    private static Task<PluginResult> HandlePing(CommandContext ctx)
        => Task.FromResult(Pong);

    private static Task<PluginResult> HandlePing(MessageContext ctx)
        => Task.FromResult(Pong);
}
