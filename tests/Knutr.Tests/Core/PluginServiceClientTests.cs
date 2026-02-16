using System.Net;
using System.Text.Json;
using FluentAssertions;
using Knutr.Core.PluginServices;
using Knutr.Sdk;
using Knutr.Sdk.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Knutr.Tests.Core;

public class PluginServiceClientTests
{
    private static PluginServiceClient CreateClient(FakeHttpMessageHandler handler, PluginServiceOptions? options = null)
    {
        var httpFactory = new FakeHttpClientFactory(handler);
        return new PluginServiceClient(
            httpFactory,
            Options.Create(options ?? new PluginServiceOptions()),
            NullLogger<PluginServiceClient>.Instance);
    }

    private static PluginServiceEntry MakeEntry(string name = "test-svc") => new()
    {
        ServiceName = name,
        BaseUrl = "http://test:8080",
        Manifest = new PluginManifest { Name = name, Version = "1.0.0" }
    };

    // ── FetchManifestAsync ──

    [Fact]
    public async Task FetchManifest_ReturnsManifest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { Name = "TestPlugin", Version = "2.0.0", SupportsScan = true }));

        var client = CreateClient(handler);
        var manifest = await client.FetchManifestAsync("http://test:8080");

        manifest.Should().NotBeNull();
        manifest!.Name.Should().Be("TestPlugin");
        manifest.Version.Should().Be("2.0.0");
    }

    [Fact]
    public async Task FetchManifest_HttpError_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
        var client = CreateClient(handler);

        var manifest = await client.FetchManifestAsync("http://test:8080");
        manifest.Should().BeNull();
    }

    // ── ExecuteAsync ──

    [Fact]
    public async Task Execute_ReturnsResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { Success = true, Text = "pong" }));

        var client = CreateClient(handler);
        var request = new PluginExecuteRequest { Command = "ping", Args = [], RawText = "", UserId = "U1", ChannelId = "C1" };
        var response = await client.ExecuteAsync(MakeEntry(), request);

        response.Success.Should().BeTrue();
        response.Text.Should().Be("pong");
    }

    [Fact]
    public async Task Execute_HttpError_ReturnsFail()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
        var client = CreateClient(handler);

        var request = new PluginExecuteRequest { Command = "ping", Args = [], RawText = "", UserId = "U1", ChannelId = "C1" };
        var response = await client.ExecuteAsync(MakeEntry(), request);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("test-svc");
    }

    // ── ScanAsync ──

    [Fact]
    public async Task Scan_WithResponse_ReturnsResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { Success = true, Text = "found TLA", Reactions = new[] { "eyes" } }));

        var client = CreateClient(handler);
        var request = new PluginScanRequest { Text = "SRE is important", UserId = "U1", ChannelId = "C1" };
        var response = await client.ScanAsync(MakeEntry(), request);

        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.Text.Should().Be("found TLA");
    }

    [Fact]
    public async Task Scan_NoContent_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent));

        var client = CreateClient(handler);
        var request = new PluginScanRequest { Text = "hello", UserId = "U1", ChannelId = "C1" };
        var response = await client.ScanAsync(MakeEntry(), request);

        response.Should().BeNull();
    }

    [Fact]
    public async Task Scan_HttpError_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
        var client = CreateClient(handler);

        var request = new PluginScanRequest { Text = "hello", UserId = "U1", ChannelId = "C1" };
        var response = await client.ScanAsync(MakeEntry(), request);

        response.Should().BeNull();
    }

    // ── ResolveServiceUrl ──

    [Fact]
    public void ResolveServiceUrl_ExplicitEndpoint_ReturnsIt()
    {
        var opts = new PluginServiceOptions
        {
            Endpoints = new Dictionary<string, string> { ["joke"] = "http://localhost:5100/" }
        };
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, ""), opts);

        client.ResolveServiceUrl("joke").Should().Be("http://localhost:5100");
    }

    [Fact]
    public void ResolveServiceUrl_NoEndpoint_ReturnsDnsUrl()
    {
        var opts = new PluginServiceOptions { Namespace = "knutr" };
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, ""), opts);

        client.ResolveServiceUrl("sentinel").Should().Be("http://knutr-plugin-sentinel.knutr.svc.cluster.local");
    }
}
