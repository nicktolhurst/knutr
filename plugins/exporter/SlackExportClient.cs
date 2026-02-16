using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Knutr.Plugins.Exporter;

public sealed record SlackMessage(string Ts, string? ThreadTs, string UserId, string Text, string? EditedTs, int ReplyCount);
public sealed record SlackMessagesPage(List<SlackMessage> Messages, string? NextCursor, bool Ok);
public sealed record SlackUserInfo(string UserId, string? DisplayName, string? RealName, bool IsBot);

public sealed class SlackExportClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ExporterSlackOptions> slackOptions,
    ILogger<SlackExportClient> log)
{
    private readonly ExporterSlackOptions _opts = slackOptions.Value;

    public async Task<SlackMessagesPage> FetchHistoryPageAsync(
        string channelId, string? oldest = null, string? latest = null, string? cursor = null, int? limit = null, CancellationToken ct = default)
    {
        var url = $"{_opts.ConversationsHistoryUrl}?channel={channelId}";
        if (oldest is not null) url += $"&oldest={oldest}";
        if (latest is not null) url += $"&latest={latest}";
        if (cursor is not null) url += $"&cursor={cursor}";
        url += $"&limit={limit ?? 200}";

        return await FetchMessagesAsync(url, ct);
    }

    public async Task<SlackMessagesPage> FetchRepliesPageAsync(
        string channelId, string threadTs, string? cursor = null, int? limit = null, CancellationToken ct = default)
    {
        var url = $"{_opts.ConversationsRepliesUrl}?channel={channelId}&ts={threadTs}";
        if (cursor is not null) url += $"&cursor={cursor}";
        url += $"&limit={limit ?? 200}";

        return await FetchMessagesAsync(url, ct);
    }

    public async Task<SlackUserInfo?> FetchUserInfoAsync(string userId, CancellationToken ct = default)
    {
        var url = $"{_opts.UsersInfoUrl}?user={userId}";
        var http = httpClientFactory.CreateClient("slack-exporter");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            log.LogWarning("Slack users.info failed: {Status} {Body}", res.StatusCode, body);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                log.LogWarning("Slack users.info error: {Error}", error);
                return null;
            }

            var user = root.GetProperty("user");
            var profile = user.GetProperty("profile");

            return new SlackUserInfo(
                UserId: userId,
                DisplayName: profile.TryGetProperty("display_name", out var dn) ? dn.GetString() : null,
                RealName: profile.TryGetProperty("real_name", out var rn) ? rn.GetString() : null,
                IsBot: user.TryGetProperty("is_bot", out var ib) && ib.GetBoolean()
            );
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Failed to parse Slack users.info response");
            return null;
        }
    }

    private async Task<SlackMessagesPage> FetchMessagesAsync(string url, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("slack-exporter");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            log.LogWarning("Slack API request failed: {Status} {Body}", res.StatusCode, body);
            return new SlackMessagesPage([], null, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                log.LogWarning("Slack API error: {Error}", error);
                return new SlackMessagesPage([], null, false);
            }

            var messages = new List<SlackMessage>();

            if (root.TryGetProperty("messages", out var messagesArray))
            {
                foreach (var msg in messagesArray.EnumerateArray())
                {
                    // Skip non-user messages (subtypes like channel_join, bot_message, etc.)
                    if (msg.TryGetProperty("subtype", out _))
                        continue;

                    var ts = msg.GetProperty("ts").GetString()!;
                    var threadTs = msg.TryGetProperty("thread_ts", out var tt) ? tt.GetString() : null;
                    var userId = msg.TryGetProperty("user", out var u) ? u.GetString()! : "unknown";
                    var text = msg.TryGetProperty("text", out var t) ? t.GetString()! : "";
                    var editedTs = msg.TryGetProperty("edited", out var edited) && edited.TryGetProperty("ts", out var ets)
                        ? ets.GetString() : null;
                    var replyCount = msg.TryGetProperty("reply_count", out var rc) ? rc.GetInt32() : 0;

                    messages.Add(new SlackMessage(ts, threadTs, userId, text, editedTs, replyCount));
                }
            }

            string? nextCursor = null;
            if (root.TryGetProperty("response_metadata", out var meta)
                && meta.TryGetProperty("next_cursor", out var nc))
            {
                var cursor = nc.GetString();
                if (!string.IsNullOrEmpty(cursor))
                    nextCursor = cursor;
            }

            return new SlackMessagesPage(messages, nextCursor, true);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Failed to parse Slack API response");
            return new SlackMessagesPage([], null, false);
        }
    }
}
