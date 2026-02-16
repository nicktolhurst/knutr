using Knutr.Plugins.Exporter.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Knutr.Plugins.Exporter;

public sealed class ExportService(
    IDbContextFactory<ExporterDbContext> dbFactory,
    SlackExportClient slackClient,
    IOptions<ExporterOptions> options,
    ILogger<ExportService> log)
{
    private readonly ExporterOptions _opts = options.Value;

    public async Task TryUpsertFromScanAsync(string channelId, string messageTs, string? threadTs, string userId, string text, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var export = await db.ChannelExports.FindAsync([channelId], ct);
        if (export is null || export.Status is "paused" or "error")
            return;

        await UpsertMessageAsync(db, channelId, messageTs, threadTs, userId, text, editedTs: null, ct);
        await EnsureUserAsync(db, userId, ct);
    }

    public async Task RunInitialExportAsync(string channelId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var export = await db.ChannelExports.FindAsync([channelId], ct);
        if (export is null) return;

        export.Status = "syncing";
        export.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Starting initial export for channel {ChannelId}", channelId);

        var threadParents = new List<string>();
        string? latestTs = null;
        string? cursor = null;

        try
        {
            // Phase 1: Paginate conversations.history
            do
            {
                var page = await slackClient.FetchHistoryPageAsync(channelId, cursor: cursor, limit: _opts.PageSize, ct: ct);
                if (!page.Ok)
                {
                    log.LogWarning("Failed to fetch history for {ChannelId}, aborting initial export", channelId);
                    export.Status = "error";
                    export.ErrorMessage = "Failed to fetch conversation history";
                    export.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return;
                }

                foreach (var msg in page.Messages)
                {
                    await UpsertMessageAsync(db, channelId, msg.Ts, msg.ThreadTs, msg.UserId, msg.Text, msg.EditedTs, ct);
                    await EnsureUserAsync(db, msg.UserId, ct);

                    if (msg.ReplyCount > 0)
                        threadParents.Add(msg.Ts);

                    if (latestTs is null || string.CompareOrdinal(msg.Ts, latestTs) > 0)
                        latestTs = msg.Ts;
                }

                cursor = page.NextCursor;
                await Task.Delay(_opts.RateLimitDelayMs, ct);
            }
            while (cursor is not null);

            log.LogInformation("Phase 1 complete for {ChannelId}: found {ThreadCount} threads", channelId, threadParents.Count);

            // Phase 2: Fetch thread replies
            foreach (var parentTs in threadParents)
            {
                string? threadCursor = null;
                do
                {
                    var page = await slackClient.FetchRepliesPageAsync(channelId, parentTs, cursor: threadCursor, limit: _opts.PageSize, ct: ct);
                    if (!page.Ok)
                    {
                        log.LogWarning("Failed to fetch replies for thread {ThreadTs} in {ChannelId}", parentTs, channelId);
                        break;
                    }

                    foreach (var msg in page.Messages)
                    {
                        await UpsertMessageAsync(db, channelId, msg.Ts, msg.ThreadTs, msg.UserId, msg.Text, msg.EditedTs, ct);
                        await EnsureUserAsync(db, msg.UserId, ct);

                        if (latestTs is null || string.CompareOrdinal(msg.Ts, latestTs) > 0)
                            latestTs = msg.Ts;
                    }

                    threadCursor = page.NextCursor;
                    await Task.Delay(_opts.RateLimitDelayMs, ct);
                }
                while (threadCursor is not null);
            }

            export.Status = "active";
            export.LastSyncTs = latestTs;
            export.ErrorMessage = null;
            export.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            log.LogInformation("Initial export complete for {ChannelId}, LastSyncTs={LastSyncTs}", channelId, latestTs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Initial export failed for {ChannelId}", channelId);
            export.Status = "error";
            export.ErrorMessage = ex.Message;
            export.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task RunIncrementalSyncAsync(string channelId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var export = await db.ChannelExports.FindAsync([channelId], ct);
        if (export is null || export.Status != "active") return;

        log.LogDebug("Incremental sync for {ChannelId} since {LastSyncTs}", channelId, export.LastSyncTs);

        var threadParents = new List<string>();
        string? latestTs = export.LastSyncTs;
        string? cursor = null;

        try
        {
            do
            {
                var page = await slackClient.FetchHistoryPageAsync(channelId, oldest: export.LastSyncTs, cursor: cursor, limit: _opts.PageSize, ct: ct);
                if (!page.Ok)
                {
                    log.LogWarning("Incremental sync fetch failed for {ChannelId}, will retry next cycle", channelId);
                    return;
                }

                foreach (var msg in page.Messages)
                {
                    await UpsertMessageAsync(db, channelId, msg.Ts, msg.ThreadTs, msg.UserId, msg.Text, msg.EditedTs, ct);
                    await EnsureUserAsync(db, msg.UserId, ct);

                    if (msg.ReplyCount > 0)
                        threadParents.Add(msg.Ts);

                    if (latestTs is null || string.CompareOrdinal(msg.Ts, latestTs) > 0)
                        latestTs = msg.Ts;
                }

                cursor = page.NextCursor;
                await Task.Delay(_opts.RateLimitDelayMs, ct);
            }
            while (cursor is not null);

            foreach (var parentTs in threadParents)
            {
                string? threadCursor = null;
                do
                {
                    var page = await slackClient.FetchRepliesPageAsync(channelId, parentTs, cursor: threadCursor, limit: _opts.PageSize, ct: ct);
                    if (!page.Ok) break;

                    foreach (var msg in page.Messages)
                    {
                        await UpsertMessageAsync(db, channelId, msg.Ts, msg.ThreadTs, msg.UserId, msg.Text, msg.EditedTs, ct);
                        await EnsureUserAsync(db, msg.UserId, ct);

                        if (latestTs is null || string.CompareOrdinal(msg.Ts, latestTs) > 0)
                            latestTs = msg.Ts;
                    }

                    threadCursor = page.NextCursor;
                    await Task.Delay(_opts.RateLimitDelayMs, ct);
                }
                while (threadCursor is not null);
            }

            export.LastSyncTs = latestTs;
            export.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            log.LogDebug("Incremental sync complete for {ChannelId}, LastSyncTs={LastSyncTs}", channelId, latestTs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Incremental sync failed for {ChannelId}, will retry next cycle", channelId);
        }
    }

    private static async Task UpsertMessageAsync(
        ExporterDbContext db, string channelId, string messageTs, string? threadTs,
        string userId, string text, string? editedTs, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO exported_messages (channel_id, message_ts, thread_ts, user_id, text, edited_ts, imported_at)
            VALUES ({channelId}, {messageTs}, {threadTs}, {userId}, {text}, {editedTs}, {now})
            ON CONFLICT (channel_id, message_ts)
            DO UPDATE SET text = EXCLUDED.text, edited_ts = EXCLUDED.edited_ts, imported_at = EXCLUDED.imported_at
            """, ct);
    }

    private async Task EnsureUserAsync(ExporterDbContext db, string userId, CancellationToken ct)
    {
        var existing = await db.ExportedUsers.FindAsync([userId], ct);
        if (existing is not null && existing.UpdatedAt > DateTimeOffset.UtcNow.AddHours(-24))
            return;

        var info = await slackClient.FetchUserInfoAsync(userId, ct);
        if (info is null) return;

        if (existing is null)
        {
            db.ExportedUsers.Add(new ExportedUser
            {
                UserId = userId,
                DisplayName = info.DisplayName,
                RealName = info.RealName,
                IsBot = info.IsBot,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.DisplayName = info.DisplayName;
            existing.RealName = info.RealName;
            existing.IsBot = info.IsBot;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
