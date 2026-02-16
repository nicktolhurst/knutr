using Knutr.Sdk;

namespace Knutr.Plugins.PingPong;

public sealed class PingPongHandler(ILogger<PingPongHandler> log) : IPluginHandler
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
    {
        log.LogInformation("Received a ping.");
        log.LogInformation("Sending a pong.");
        return Task.FromResult(PluginExecuteResponse.Ok("pong"));
    }
}
