using System.Net;
using FluentAssertions;
using Knutr.Sdk;
using Knutr.Sdk.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Knutr.Plugins.Summariser.Tests;

public class SummariserHandlerTests
{
    private readonly SummariserHandler _handler;

    public SummariserHandlerTests()
    {
        var httpFactory = new FakeHttpClientFactory(new FakeHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ok":true,"messageTs":"1234"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        }));

        var summariserOptions = Options.Create(new SummariserOptions());
        var ollamaOptions = Options.Create(new SummariserOllamaOptions());

        var exporterClient = new ExporterClient(httpFactory, summariserOptions, NullLogger<ExporterClient>.Instance);
        var ollama = new OllamaHelper(httpFactory, ollamaOptions, NullLogger<OllamaHelper>.Instance);
        var corePost = new CorePostClient(httpFactory, summariserOptions, NullLogger<CorePostClient>.Instance);
        var summaryService = new SummaryService(exporterClient, ollama, corePost, summariserOptions, NullLogger<SummaryService>.Instance);

        _handler = new SummariserHandler(summaryService, NullLogger<SummariserHandler>.Instance);
    }

    [Fact]
    public void Manifest_HasExpectedIdentity()
    {
        var manifest = _handler.GetManifest();
        manifest.Name.Should().Be("Summariser");
        manifest.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Manifest_HasSubcommand()
    {
        var manifest = _handler.GetManifest();
        manifest.Subcommands.Should().ContainSingle()
            .Which.Name.Should().Be("summarise");
    }

    [Fact]
    public void Manifest_DependsOnExporter()
    {
        var manifest = _handler.GetManifest();
        manifest.Dependencies.Should().Contain("exporter");
    }

    [Fact]
    public void Manifest_DoesNotSupportScan()
    {
        var manifest = _handler.GetManifest();
        manifest.SupportsScan.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_NoArgs_ReturnsUsage()
    {
        var request = A.ExecuteRequest.WithArgs().Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("Usage");
    }

    [Fact]
    public async Task Execute_InvalidPreset_ReturnsUsage()
    {
        var request = A.ExecuteRequest.WithArgs("invalid").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("Usage");
    }

    [Fact]
    public async Task Execute_Lessons_ReturnsAcknowledgment()
    {
        var request = A.ExecuteRequest.WithArgs("lessons").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
        response.Text.Should().Contain("lessons");
    }

    [Fact]
    public async Task Execute_Backlog_ReturnsAcknowledgment()
    {
        var request = A.ExecuteRequest.WithArgs("backlog").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
        response.Text.Should().Contain("backlog");
    }

    [Fact]
    public async Task Execute_WithChannelRef_ReturnsAcknowledgment()
    {
        var request = A.ExecuteRequest.WithArgs("lessons", "<#C123|general>").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
        response.Text.Should().Contain("C123");
    }

    [Fact]
    public async Task Execute_InvalidChannelRef_ReturnsError()
    {
        var request = A.ExecuteRequest.WithArgs("lessons", "badref").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("Invalid channel reference");
    }

    [Fact]
    public async Task Scan_ReturnsNull()
    {
        var request = A.ScanRequest.WithText("hello").Build();
        var response = await _handler.ScanAsync(request);
        response.Should().BeNull();
    }
}
