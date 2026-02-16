using System.Net;
using FluentAssertions;
using Knutr.Plugins.Exporter.Data;
using Knutr.Sdk;
using Knutr.Sdk.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Knutr.Plugins.Exporter.Tests;

public class ExporterHandlerTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly ExporterHandler _handler;

    public ExporterHandlerTests()
    {
        _dbFactory = new InMemoryDbContextFactory($"ExporterTests_{Guid.NewGuid()}");
        var httpFactory = new FakeHttpClientFactory(HttpStatusCode.OK, """{"ok":true,"messages":[],"response_metadata":{"next_cursor":""}}""");
        var slackClient = new SlackExportClient(httpFactory, Options.Create(new ExporterSlackOptions()), NullLogger<SlackExportClient>.Instance);
        var exportService = new ExportService(_dbFactory, slackClient, Options.Create(new ExporterOptions()), NullLogger<ExportService>.Instance);
        _handler = new ExporterHandler(exportService, _dbFactory, NullLogger<ExporterHandler>.Instance);
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Manifest_HasExpectedIdentity()
    {
        var manifest = _handler.GetManifest();
        manifest.Name.Should().Be("Exporter");
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
            .Which.Name.Should().Be("export");
    }

    [Fact]
    public async Task Execute_UnknownAction_ReturnsHelp()
    {
        var request = A.ExecuteRequest.WithArgs("foobar").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("Unknown export command");
    }

    [Fact]
    public async Task Execute_This_CreatesExport()
    {
        var request = A.ExecuteRequest.WithArgs("this").WithChannelId("C_NEW").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Success.Should().BeTrue();
        response.Text.Should().Contain("Export started");

        await using var db = _dbFactory.CreateDbContext();
        var export = await db.ChannelExports.FindAsync("C_NEW");
        export.Should().NotBeNull();
        export!.Status.Should().Be("pending_initial");
    }

    [Fact]
    public async Task Execute_This_AlreadyExported_ShowsStatus()
    {
        await SeedExport("C_EXISTING", "active");

        var request = A.ExecuteRequest.WithArgs("this").WithChannelId("C_EXISTING").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("already being exported");
    }

    [Fact]
    public async Task Execute_This_ReenablesPaused()
    {
        await SeedExport("C_PAUSED", "paused");

        var request = A.ExecuteRequest.WithArgs("this").WithChannelId("C_PAUSED").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("re-enabled");

        await using var db = _dbFactory.CreateDbContext();
        var export = await db.ChannelExports.FindAsync("C_PAUSED");
        export!.Status.Should().Be("pending_initial");
    }

    [Fact]
    public async Task Execute_Stop_PausesExport()
    {
        await SeedExport("C_STOP", "active");

        var request = A.ExecuteRequest.WithArgs("stop").WithChannelId("C_STOP").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("paused");

        await using var db = _dbFactory.CreateDbContext();
        var export = await db.ChannelExports.FindAsync("C_STOP");
        export!.Status.Should().Be("paused");
    }

    [Fact]
    public async Task Execute_Stop_NotExported_ShowsMessage()
    {
        var request = A.ExecuteRequest.WithArgs("stop").WithChannelId("C_NONE").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("not being exported");
    }

    [Fact]
    public async Task Execute_Status_NoExports()
    {
        var request = A.ExecuteRequest.WithArgs("status").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("No channels");
    }

    [Fact]
    public async Task Execute_Status_ShowsExports()
    {
        await SeedExport("C_STATUS", "active");

        var request = A.ExecuteRequest.WithArgs("status").Build();
        var response = await _handler.ExecuteAsync(request);
        response.Text.Should().Contain("C_STATUS");
        response.Text.Should().Contain("active");
    }

    [Fact]
    public async Task Scan_NoMessageTs_ReturnsNull()
    {
        var request = A.ScanRequest.WithText("hello").Build();
        var response = await _handler.ScanAsync(request);
        response.Should().BeNull();
    }

    [Fact]
    public async Task Scan_WithMessageTs_NoExport_ReturnsNull()
    {
        var request = A.ScanRequest
            .WithText("hello")
            .WithMessageTs("1234567890.000001")
            .WithChannelId("C_NOSUCH")
            .Build();
        var response = await _handler.ScanAsync(request);
        response.Should().BeNull();
    }

    private async Task SeedExport(string channelId, string status)
    {
        await using var db = _dbFactory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        db.ChannelExports.Add(new ChannelExport
        {
            ChannelId = channelId,
            Status = status,
            RequestedByUserId = "U_SEED",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private sealed class InMemoryDbContextFactory : IDbContextFactory<ExporterDbContext>, IDisposable
    {
        private readonly DbContextOptions<ExporterDbContext> _options;

        public InMemoryDbContextFactory(string dbName)
        {
            _options = new DbContextOptionsBuilder<ExporterDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

            using var db = CreateDbContext();
            db.Database.EnsureCreated();
        }

        public ExporterDbContext CreateDbContext() => new(_options);

        public void Dispose()
        {
            using var db = CreateDbContext();
            db.Database.EnsureDeleted();
        }
    }
}
