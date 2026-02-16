using FluentAssertions;
using Knutr.Sdk;
using Knutr.Sdk.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Knutr.Plugins.JargonBuster.Tests;

public class JargonBusterHandlerTests
{
    private static JargonBusterHandler CreateHandler(bool useNlp = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JargonBuster:UseNlp"] = useNlp.ToString()
            })
            .Build();
        return new JargonBusterHandler(NullLogger<JargonBusterHandler>.Instance, config);
    }

    private readonly JargonBusterHandler _handler = CreateHandler();

    [Fact]
    public void Manifest_HasExpectedIdentity()
    {
        var manifest = _handler.GetManifest();
        manifest.Name.Should().Be("JargonBuster");
        manifest.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Manifest_SupportsScan()
    {
        var manifest = _handler.GetManifest();
        manifest.SupportsScan.Should().BeTrue();
    }

    [Fact]
    public void Manifest_HasNoSlashCommands()
    {
        var manifest = _handler.GetManifest();
        manifest.SlashCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_ReturnsFail()
    {
        var request = A.ExecuteRequest.Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Scan_NoTlas_ReturnsNull()
    {
        var request = A.ScanRequest.WithText("hello world no acronyms here").Build();
        var response = await _handler.ScanAsync(request);
        response.Should().BeNull();
    }

    [Fact]
    public async Task Scan_MatchesTla_ReturnsReaction()
    {
        var request = A.ScanRequest.WithText("We need better SRE practices").Build();
        var response = await _handler.ScanAsync(request);
        response.Should().NotBeNull();
        response!.Reactions.Should().Contain("knutr-teach-me");
        response.Text.Should().BeNull();
    }

    [Fact]
    public async Task Scan_DeduplicatesSameContext()
    {
        var handler = CreateHandler();
        var request = A.ScanRequest.WithText("SRE is important").WithChannelId("C_DEDUP").Build();

        var first = await handler.ScanAsync(request);
        first.Should().NotBeNull();

        var second = await handler.ScanAsync(request);
        second.Should().BeNull();
    }

    [Fact]
    public async Task Scan_ReactionTriggered_ReturnsDefinitions()
    {
        var request = A.ScanRequest
            .WithText("SRE is important")
            .WithThreadTs("_reaction_123456")
            .Build();
        var response = await _handler.ScanAsync(request);
        response.Should().NotBeNull();
        response!.Text.Should().Contain("SRE");
    }

    [Fact]
    public async Task Scan_ReactionTriggered_IncludesNlStyle()
    {
        var handler = CreateHandler(useNlp: true);
        var request = A.ScanRequest
            .WithText("SRE is important")
            .WithThreadTs("_reaction_123456")
            .Build();
        var response = await handler.ScanAsync(request);
        response.Should().NotBeNull();
        response!.UseNaturalLanguage.Should().BeTrue();
        response.NaturalLanguageStyle.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Scan_DifferentContexts_NoDedupe()
    {
        var handler = CreateHandler();
        var request1 = A.ScanRequest.WithText("SRE is important").WithChannelId("C1").Build();
        var request2 = A.ScanRequest.WithText("SRE is important").WithChannelId("C2").Build();

        var first = await handler.ScanAsync(request1);
        first.Should().NotBeNull();

        var second = await handler.ScanAsync(request2);
        second.Should().NotBeNull();
    }
}
