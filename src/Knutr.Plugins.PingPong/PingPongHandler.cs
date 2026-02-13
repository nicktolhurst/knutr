using Knutr.Sdk;

namespace Knutr.Plugins.PingPong;

public sealed class PingPongHandler : IPluginHandler
{
    public PluginManifest GetManifest() => new()
    {
        Name = "PingPong",
        Version = "1.0.0",
        Description = "Responds to ping with pong",
        SlashCommands =
        [
            new() { Command = "ping", Description = "Ping the bot" }
        ],
    };

    public Task<PluginExecuteResponse> ExecuteAsync(PluginExecuteRequest request, CancellationToken ct = default)
        => Task.FromResult(PluginExecuteResponse.Ok("pong"));
}
