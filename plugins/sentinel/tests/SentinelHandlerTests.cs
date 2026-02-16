using System.Net;
using FluentAssertions;
using Knutr.Sdk;
using Knutr.Sdk.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Knutr.Plugins.Sentinel.Tests;

public class SentinelHandlerTests
{
    private readonly SentinelState _state = new();
    private readonly SentinelHandler _handler;

    public SentinelHandlerTests()
    {
        var ollamaJson = """{"response": "{\"relevance\": 1.0, \"nudge\": \"\"}"}""";
        var httpFactory = new FakeHttpClientFactory(HttpStatusCode.OK, ollamaJson);
        var ollamaOptions = Options.Create(new OllamaOptions());
        var ollama = new OllamaHelper(httpFactory, ollamaOptions, NullLogger<OllamaHelper>.Instance);
        var drift = new DriftAnalyzer(ollama, NullLogger<DriftAnalyzer>.Instance, _state);
        var commands = new CommandDetector(_state, drift, NullLogger<CommandDetector>.Instance);
        _handler = new SentinelHandler(drift, commands, _state, NullLogger<SentinelHandler>.Instance);
    }

    [Fact]
    public void Manifest_HasExpectedIdentity()
    {
        var manifest = _handler.GetManifest();
        manifest.Name.Should().Be("Sentinel");
        manifest.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Manifest_SupportsScan()
    {
        var manifest = _handler.GetManifest();
        manifest.SupportsScan.Should().BeTrue();
    }

    [Fact]
    public void Manifest_HasSubcommand()
    {
        var manifest = _handler.GetManifest();
        manifest.Subcommands.Should().ContainSingle()
            .Which.Name.Should().Be("sentinel");
    }

    [Fact]
    public async Task Execute_Status_ShowsConfig()
    {
        var request = A.ExecuteRequest.WithArgs("status").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
        response.Ephemeral.Should().BeTrue();
        response.Text.Should().Contain("threshold");
    }

    [Fact]
    public async Task Execute_Set_UpdatesConfig()
    {
        var request = A.ExecuteRequest.WithArgs("set", "threshold=0.5").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
        _state.GetConfig("threshold").Should().Be("0.5");
    }

    [Fact]
    public async Task Execute_Set_MissingArgs_ShowsUsage()
    {
        var request = A.ExecuteRequest.WithArgs("set").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("Usage");
    }

    [Fact]
    public async Task Execute_Get_ReturnsValue()
    {
        var request = A.ExecuteRequest.WithArgs("get", "threshold").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
        response.Text.Should().Contain("0.7");
    }

    [Fact]
    public async Task Execute_Get_MissingArgs_ShowsUsage()
    {
        var request = A.ExecuteRequest.WithArgs("get").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("Usage");
    }

    [Fact]
    public async Task Execute_Watch_WatchesChannel()
    {
        var request = A.ExecuteRequest.WithChannelId("C_WATCH").Build();
        var execRequest = A.ExecuteRequest.WithArgs("watch").WithChannelId("C_WATCH").Build();
        var response = await _handler.ExecuteAsync(execRequest);
        response.Success.Should().BeTrue();
        response.Text.Should().Contain("watching");
        _state.IsChannelWatched("C_WATCH").Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Unwatch_UnwatchesChannel()
    {
        _state.WatchChannel("C_UNWATCH", "U1");
        var request = A.ExecuteRequest.WithArgs("unwatch").WithChannelId("C_UNWATCH").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
        response.Text.Should().Contain("stopped");
        _state.IsChannelWatched("C_UNWATCH").Should().BeFalse();
    }

    [Fact]
    public async Task Execute_Unknown_ReturnsHelp()
    {
        var request = A.ExecuteRequest.WithArgs("foobar").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("Unknown sentinel command");
    }

    [Fact]
    public async Task Scan_NoThreadContext_ReturnsNull()
    {
        var request = A.ScanRequest.WithText("hello world").Build();
        var response = await _handler.ScanAsync(request);
        response.Should().BeNull();
    }

    [Fact]
    public async Task Scan_WatchedChannel_AutoWatchesThread()
    {
        _state.WatchChannel("C_AUTO", "U1");

        var request = A.ScanRequest
            .WithText("Let's discuss the new API design")
            .WithChannelId("C_AUTO")
            .WithThreadTs("T_NEW")
            .Build();

        await _handler.ScanAsync(request);

        _state.IsThreadWatched("C_AUTO", "T_NEW").Should().BeTrue();
        _state.GetBuffer("C_AUTO", "T_NEW").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Scan_UnwatchedThread_ReturnsNull()
    {
        var request = A.ScanRequest
            .WithText("hello")
            .WithChannelId("C_UNWATCHED")
            .WithThreadTs("T_UNWATCHED")
            .Build();
        var response = await _handler.ScanAsync(request);
        response.Should().BeNull();
    }

    [Fact]
    public async Task Scan_BuffersBeforeAnalysis()
    {
        _state.WatchThread("C_BUF", "T_BUF", "original topic", "U1");

        var request = A.ScanRequest
            .WithText("first reply")
            .WithChannelId("C_BUF")
            .WithThreadTs("T_BUF")
            .Build();

        var response = await _handler.ScanAsync(request);
        response.Should().BeNull();

        _state.GetBuffer("C_BUF", "T_BUF").Should().HaveCount(1);
    }
}
