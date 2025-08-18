namespace Knutr.Plugins.PingPong;

using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.Events;

public sealed class Plugin : IBotPlugin
{
    public string Name => "PingPong";

    public void Configure(ICommandBuilder commands)
    {
        commands
            .Slash("ping", (CommandContext ctx) => Task.FromResult(PluginResult.SkipNl(new Reply("pong"))))
            .Message("ping", ["Ping", "PING"], ctx => Task.FromResult(PluginResult.SkipNl(new Reply("pong"))));
    }
}
