using Knutr.Plugins.Exporter.Data;
using Microsoft.EntityFrameworkCore;

namespace Knutr.Plugins.Exporter;

public static class ExporterApiEndpoints
{
    public static IEndpointRouteBuilder MapExporterApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet("/channels", async (IDbContextFactory<ExporterDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var exports = await db.ChannelExports.ToListAsync(ct);
            var results = new List<object>();

            foreach (var export in exports)
            {
                var messageCount = await db.ExportedMessages.CountAsync(m => m.ChannelId == export.ChannelId, ct);
                results.Add(new
                {
                    export.ChannelId,
                    export.Status,
                    export.LastSyncTs,
                    MessageCount = messageCount,
                });
            }

            return Results.Ok(results);
        });

        api.MapGet("/channels/{channelId}/messages", async (
            string channelId, string? since, string? until, int? limit, int? offset,
            IDbContextFactory<ExporterDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var cappedLimit = Math.Min(limit ?? 100, 1000);
            var skip = offset ?? 0;

            var query = db.ExportedMessages
                .Where(m => m.ChannelId == channelId);

            if (since is not null)
                query = query.Where(m => string.Compare(m.MessageTs, since) > 0);
            if (until is not null)
                query = query.Where(m => string.Compare(m.MessageTs, until) <= 0);

            var messages = await query
                .OrderBy(m => m.MessageTs)
                .Skip(skip)
                .Take(cappedLimit)
                .Select(m => new
                {
                    m.ChannelId,
                    m.MessageTs,
                    m.ThreadTs,
                    m.UserId,
                    m.Text,
                    m.EditedTs,
                    m.ImportedAt,
                })
                .ToListAsync(ct);

            return Results.Ok(messages);
        });

        api.MapGet("/channels/{channelId}/threads", async (
            string channelId, int? limit, int? offset,
            IDbContextFactory<ExporterDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var cappedLimit = Math.Min(limit ?? 50, 1000);
            var skip = offset ?? 0;

            var threads = await db.ExportedMessages
                .Where(m => m.ChannelId == channelId && m.ThreadTs != null)
                .GroupBy(m => m.ThreadTs)
                .Select(g => new
                {
                    ThreadTs = g.Key,
                    ReplyCount = g.Count(),
                    LatestReplyTs = g.Max(m => m.MessageTs),
                })
                .OrderByDescending(t => t.LatestReplyTs)
                .Skip(skip)
                .Take(cappedLimit)
                .ToListAsync(ct);

            return Results.Ok(threads);
        });

        api.MapGet("/channels/{channelId}/threads/{threadTs}", async (
            string channelId, string threadTs,
            IDbContextFactory<ExporterDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var messages = await db.ExportedMessages
                .Where(m => m.ChannelId == channelId && (m.MessageTs == threadTs || m.ThreadTs == threadTs))
                .OrderBy(m => m.MessageTs)
                .Select(m => new
                {
                    m.ChannelId,
                    m.MessageTs,
                    m.ThreadTs,
                    m.UserId,
                    m.Text,
                    m.EditedTs,
                    m.ImportedAt,
                })
                .ToListAsync(ct);

            return Results.Ok(messages);
        });

        api.MapGet("/users/{userId}", async (
            string userId,
            IDbContextFactory<ExporterDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var user = await db.ExportedUsers.FindAsync([userId], ct);
            if (user is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                user.UserId,
                user.DisplayName,
                user.RealName,
                user.IsBot,
                user.UpdatedAt,
            });
        });

        return endpoints;
    }
}
