using FluentAssertions;
using Knutr.Sdk;
using Knutr.Sdk.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Knutr.Plugins.Joke.Tests;

public class JokeHandlerTests
{
    private readonly JokeHandler _handler = new(NullLogger<JokeHandler>.Instance);

    private static readonly string[] KnownJokes =
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

    [Fact]
    public void Manifest_HasExpectedIdentity()
    {
        var manifest = _handler.GetManifest();
        manifest.Name.Should().Be("Joke");
        manifest.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Manifest_DeclaresSlashCommand()
    {
        var manifest = _handler.GetManifest();
        manifest.SlashCommands.Should().ContainSingle()
            .Which.Command.Should().Be("joke");
    }

    [Fact]
    public void Manifest_DoesNotSupportScan()
    {
        var manifest = _handler.GetManifest();
        manifest.SupportsScan.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_ReturnsSuccess()
    {
        var request = A.ExecuteRequest.WithCommand("joke").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ReturnsKnownJoke()
    {
        var request = A.ExecuteRequest.WithCommand("joke").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().BeOneOf(KnownJokes);
    }

    [Fact]
    public async Task Execute_ResponseShape_IsPlain()
    {
        var request = A.ExecuteRequest.WithCommand("joke").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Ephemeral.Should().BeFalse();
        response.UseNaturalLanguage.Should().BeFalse();
        response.Reactions.Should().BeNull();
    }

    [Fact]
    public async Task Scan_ReturnsNull()
    {
        var request = A.ScanRequest.WithText("hello").Build();
        var response = await ((IPluginHandler)_handler).ScanAsync(request);
        response.Should().BeNull();
    }
}
