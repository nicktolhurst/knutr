using FluentAssertions;
using Knutr.Sdk;
using Knutr.Sdk.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Knutr.Plugins.PingPong.Tests;

public class PingPongHandlerTests
{
    private readonly PingPongHandler _handler = new(NullLogger<PingPongHandler>.Instance);

    [Fact]
    public void Manifest_HasExpectedIdentity()
    {
        var manifest = _handler.GetManifest();

        manifest.Name.Should().Be("PingPong");
        manifest.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Manifest_DeclaresSlashCommand()
    {
        var manifest = _handler.GetManifest();

        manifest.SlashCommands.Should().ContainSingle()
            .Which.Command.Should().Be("ping");
    }

    [Fact]
    public void Manifest_DoesNotSupportScan()
    {
        var manifest = _handler.GetManifest();

        manifest.SupportsScan.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_ReturnsPong()
    {
        var request = A.ExecuteRequest.WithCommand("ping").Build();

        var response = await _handler.ExecuteAsync(request);

        response.Success.Should().BeTrue();
        response.Text.Should().Be("pong");
    }

    [Fact]
    public async Task Execute_ResponseShape_IsPlain()
    {
        var request = A.ExecuteRequest.WithCommand("ping").Build();

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
