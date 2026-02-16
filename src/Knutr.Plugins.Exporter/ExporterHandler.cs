using System.Text;
using System.Text.RegularExpressions;
using Knutr.Plugins.Exporter.Data;
using Knutr.Sdk;
using Microsoft.EntityFrameworkCore;

namespace Knutr.Plugins.Exporter;

public sealed partial class ExporterHandler(
    ExportService exportService,
    IDbContextFactory<ExporterDbContext> dbFactory,
    ILogger<ExporterHandler> log) : IPluginHandler
{
    public PluginManifest GetManifest() => new()
    {
        Name = "Exporter",
        Version = "1.0.0",
        Description = "Export channel conversations to PostgreSQL for ML consumption",
        SupportsScan = true,
        Subcommands =
        [
            new() { Name = "export", Description = "Export channel conversations" }
        ],
    };

    public async Task<PluginExecuteResponse> ExecuteAsync(PluginExecuteRequest request, CancellationToken ct = default)
    {
        var args = request.Args;
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

        return action switch
        {
            "this" => await HandleStartExport(request.ChannelId, request.UserId, ct),
            "stop" => await HandleStop(request.ChannelId, ct),
            "status" => await HandleStatus(ct),
            _ when action.StartsWith("<#") => await HandleChannelReference(action, request.UserId, ct),
            _ => PluginExecuteResponse.EphemeralOk(
                "Unknown export command: `" + action + "`\n" +
                "Available: `this`, `#channel`, `stop`, `status`"),
        };
    }

    public async Task<PluginExecuteResponse?> ScanAsync(PluginScanRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.MessageTs))
            return null;

        await exportService.TryUpsertFromScanAsync(
            request.ChannelId,
            request.MessageTs,
            request.ThreadTs,
            request.UserId,
            request.Text,
            ct);

        return null;
    }

    private async Task<PluginExecuteResponse> HandleStartExport(string channelId, string userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existing = await db.ChannelExports.FindAsync([channelId], ct);
        if (existing is not null)
        {
            if (existing.Status is "paused" or "error")
            {
                existing.Status = "pending_initial";
                existing.ErrorMessage = null;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                log.LogInformation("Re-enabled export for channel {ChannelId}", channelId);
                return PluginExecuteResponse.EphemeralOk("Export re-enabled for this channel. Initial sync will begin shortly.");
            }

            return PluginExecuteResponse.EphemeralOk("This channel is already being exported (status: `" + existing.Status + "`).");
        }

        var now = DateTimeOffset.UtcNow;
        db.ChannelExports.Add(new ChannelExport
        {
            ChannelId = channelId,
            Status = "pending_initial",
            RequestedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        log.LogInformation("Export started for channel {ChannelId} by user {UserId}", channelId, userId);
        return PluginExecuteResponse.EphemeralOk("Export started for this channel. Initial sync will begin shortly.");
    }

    private async Task<PluginExecuteResponse> HandleChannelReference(string channelRef, string userId, CancellationToken ct)
    {
        var match = ChannelRefPattern().Match(channelRef);
        if (!match.Success)
            return PluginExecuteResponse.EphemeralOk("Invalid channel reference. Use `#channel` format.");

        var channelId = match.Groups[1].Value;
        return await HandleStartExport(channelId, userId, ct);
    }

    private async Task<PluginExecuteResponse> HandleStop(string channelId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var export = await db.ChannelExports.FindAsync([channelId], ct);
        if (export is null)
            return PluginExecuteResponse.EphemeralOk("This channel is not being exported.");

        export.Status = "paused";
        export.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Export paused for channel {ChannelId}", channelId);
        return PluginExecuteResponse.EphemeralOk("Export paused for this channel.");
    }

    private async Task<PluginExecuteResponse> HandleStatus(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var exports = await db.ChannelExports.ToListAsync(ct);
        if (exports.Count == 0)
            return PluginExecuteResponse.EphemeralOk("No channels are being exported.");

        var sb = new StringBuilder();
        sb.AppendLine("*Export Status*");
        sb.AppendLine();

        foreach (var export in exports)
        {
            var messageCount = await db.ExportedMessages.CountAsync(m => m.ChannelId == export.ChannelId, ct);
            sb.AppendLine($"  <#{export.ChannelId}> — `{export.Status}` — {messageCount:N0} messages");
            if (export.ErrorMessage is not null)
                sb.AppendLine($"    Error: {export.ErrorMessage}");
        }

        return PluginExecuteResponse.EphemeralOk(sb.ToString().TrimEnd());
    }

    [GeneratedRegex(@"<#(C[A-Z0-9]+)\|[^>]*>")]
    private static partial Regex ChannelRefPattern();
}
