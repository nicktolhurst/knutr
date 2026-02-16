using Knutr.Plugins.Exporter.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Knutr.Plugins.Exporter;

public sealed class ExportSyncWorker(
    ExportService exportService,
    IDbContextFactory<ExporterDbContext> dbFactory,
    IOptions<ExporterOptions> options,
    ILogger<ExportSyncWorker> log) : BackgroundService
{
    private readonly ExporterOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup delay to let DB migrations complete
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        log.LogInformation("ExportSyncWorker started, polling every {Interval} minutes", _opts.PollIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSyncCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Sync cycle failed, will retry next interval");
            }

            await Task.Delay(TimeSpan.FromMinutes(_opts.PollIntervalMinutes), stoppingToken);
        }

        log.LogInformation("ExportSyncWorker stopping");
    }

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Process pending initial exports
        var pendingChannels = await db.ChannelExports
            .Where(e => e.Status == "pending_initial")
            .Select(e => e.ChannelId)
            .ToListAsync(ct);

        foreach (var channelId in pendingChannels)
        {
            log.LogInformation("Running initial export for {ChannelId}", channelId);
            await exportService.RunInitialExportAsync(channelId, ct);
        }

        // Process active channels for incremental sync
        var activeChannels = await db.ChannelExports
            .Where(e => e.Status == "active")
            .Select(e => e.ChannelId)
            .ToListAsync(ct);

        foreach (var channelId in activeChannels)
        {
            log.LogDebug("Running incremental sync for {ChannelId}", channelId);
            await exportService.RunIncrementalSyncAsync(channelId, ct);
        }

        if (pendingChannels.Count > 0 || activeChannels.Count > 0)
            log.LogInformation("Sync cycle complete: {Pending} initial, {Active} incremental", pendingChannels.Count, activeChannels.Count);
    }
}
