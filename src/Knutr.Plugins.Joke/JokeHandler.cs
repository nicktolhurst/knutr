using Knutr.Sdk;

namespace Knutr.Plugins.Joke;

public sealed class JokeHandler : IPluginHandler
{
    private static readonly string[] Jokes =
    [
        "Why do programmers prefer dark mode? Because light attracts bugs.",
        "There are only 10 types of people in the world: those who understand binary and those who don't.",
        "A SQL query walks into a bar, sees two tables, and asks: 'Can I JOIN you?'",
        "Why do Java developers wear glasses? Because they can't C#.",
        "What's a pirate's favourite programming language? R!",
        "Why was the JavaScript developer sad? Because he didn't Node how to Express himself.",
        "How many programmers does it take to change a light bulb? None. That's a hardware problem.",
        "!false — it's funny because it's true.",
        "Why do Kubernetes admins make bad comedians? Their jokes keep getting evicted.",
        "What did the Docker container say to the VM? 'You're carrying too much baggage.'"
    ];

    public PluginManifest GetManifest() => new()
    {
        Name = "Joke",
        Version = "1.0.0",
        Description = "Tells a random programming joke",
        SlashCommands =
        [
            new() { Command = "joke", Description = "Get a random programming joke" }
        ]
    };

    public Task<PluginExecuteResponse> ExecuteAsync(PluginExecuteRequest request, CancellationToken ct = default)
    {
        var joke = Jokes[Random.Shared.Next(Jokes.Length)];
        return Task.FromResult(PluginExecuteResponse.Ok(joke));
    }
}
