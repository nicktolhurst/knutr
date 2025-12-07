namespace Knutr.Adapters.Slack;

using System.Net.Http.Json;
using System.Text.Json;
using Knutr.Abstractions.Messaging;
using Knutr.Core.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Slack implementation of IThreadedMessagingService.
/// Posts messages via the Slack API and returns the message timestamp for threading.
/// </summary>
public sealed class SlackThreadedMessagingService : IThreadedMessagingService
{
    private readonly HttpClient _http;
    private readonly SlackOptions _opts;
    private readonly ILogger<SlackThreadedMessagingService> _log;

    public SlackThreadedMessagingService(
        IHttpClientFactory httpFactory,
        IOptions<SlackOptions> opts,
        ILogger<SlackThreadedMessagingService> log)
    {
        _http = httpFactory.CreateClient("slack");
        _opts = opts.Value;
        _log = log;
    }

    public async Task<string?> PostMessageAsync(string channelId, string text, string? threadTs = null, CancellationToken ct = default)
    {
        return await PostInternalAsync(channelId, text, blocks: null, threadTs, ct);
    }

    public async Task<string?> PostBlocksAsync(string channelId, string text, object[] blocks, string? threadTs = null, CancellationToken ct = default)
    {
        return await PostInternalAsync(channelId, text, blocks, threadTs, ct);
    }

    private async Task<string?> PostInternalAsync(string channelId, string text, object[]? blocks, string? threadTs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            _log.LogWarning("Slack BotToken not configured, cannot post message");
            return null;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBase}/chat.postMessage");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var payload = new Dictionary<string, object>
        {
            ["channel"] = channelId,
            ["text"] = text,
            ["mrkdwn"] = true
        };

        if (blocks is not null)
        {
            payload["blocks"] = blocks;
        }

        if (!string.IsNullOrEmpty(threadTs))
        {
            payload["thread_ts"] = threadTs;
        }

        req.Content = JsonContent.Create(payload);

        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Slack chat.postMessage failed: {Status} {Body}", res.StatusCode, body);
            return null;
        }

        return ParseTimestamp(body);
    }

    public async Task UpdateMessageAsync(string channelId, string messageTs, string newText, CancellationToken ct = default)
    {
        await UpdateInternalAsync(channelId, messageTs, newText, blocks: null, ct);
    }

    public async Task UpdateBlocksAsync(string channelId, string messageTs, string text, object[] blocks, CancellationToken ct = default)
    {
        await UpdateInternalAsync(channelId, messageTs, text, blocks, ct);
    }

    private async Task UpdateInternalAsync(string channelId, string messageTs, string text, object[]? blocks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            _log.LogWarning("Slack BotToken not configured, cannot update message");
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBase}/chat.update");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var payload = new Dictionary<string, object>
        {
            ["channel"] = channelId,
            ["ts"] = messageTs,
            ["text"] = text,
            ["mrkdwn"] = true
        };

        if (blocks is not null)
        {
            payload["blocks"] = blocks;
        }

        req.Content = JsonContent.Create(payload);

        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Slack chat.update failed: {Status} {Body}", res.StatusCode, body);
        }
    }

    public async Task<string?> PostDmAsync(string userId, string text, object[]? blocks = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            _log.LogWarning("Slack BotToken not configured, cannot send DM");
            return null;
        }

        // First, open a conversation with the user
        var (channelId, openError) = await OpenConversationAsync(userId, ct);
        if (string.IsNullOrEmpty(channelId))
        {
            _log.LogWarning("Failed to open DM channel with user {UserId}: {Error}. " +
                "Ensure the Slack app has 'im:write' and 'users:read' OAuth scopes.",
                userId, openError ?? "unknown");
            return null;
        }

        // Then post the message to that channel
        return await PostInternalAsync(channelId, text, blocks, threadTs: null, ct);
    }

    public async Task<MessagingResult> TryPostDmAsync(string userId, string text, object[]? blocks = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            return MessagingResult.Fail("bot_token_not_configured", "Slack BotToken is not configured");
        }

        // First, open a conversation with the user
        var (success, channelId, error, errorDetail, httpStatus) = await OpenConversationWithDetailsAsync(userId, ct);
        if (!success)
        {
            _log.LogWarning("Failed to open DM channel with user {UserId}: {Error} - {Detail}",
                userId, error, errorDetail);
            return MessagingResult.Fail(error ?? "unknown", errorDetail, httpStatus);
        }

        // Then post the message to that channel
        return await PostInternalWithDetailsAsync(channelId!, text, blocks, threadTs: null, ct);
    }

    private async Task<(bool Success, string? ChannelId, string? Error, string? ErrorDetail, int? HttpStatus)> OpenConversationWithDetailsAsync(string userId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBase}/conversations.open");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var payload = new Dictionary<string, object>
        {
            ["users"] = userId
        };

        req.Content = JsonContent.Create(payload);

        HttpResponseMessage res;
        string body;
        try
        {
            res = await _http.SendAsync(req, ct);
            body = await res.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HTTP exception calling conversations.open");
            return (false, null, "http_exception", ex.ToString(), null);
        }

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Slack conversations.open failed: {Status} {Body}", res.StatusCode, body);
            return (false, null, $"http_{(int)res.StatusCode}", body, (int)res.StatusCode);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                if (root.TryGetProperty("channel", out var channel) &&
                    channel.TryGetProperty("id", out var id))
                {
                    return (true, id.GetString(), null, null, (int)res.StatusCode);
                }
                return (false, null, "no_channel_id", body, (int)res.StatusCode);
            }

            var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            _log.LogWarning("Slack conversations.open error: {Error} - Full response: {Body}", error, body);
            return (false, null, error, body, (int)res.StatusCode);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse conversations.open response: {Body}", body);
            return (false, null, "json_parse_error", $"{ex.Message}\n\nResponse: {body}", (int)res.StatusCode);
        }
    }

    private async Task<MessagingResult> PostInternalWithDetailsAsync(string channelId, string text, object[]? blocks, string? threadTs, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBase}/chat.postMessage");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var payload = new Dictionary<string, object>
        {
            ["channel"] = channelId,
            ["text"] = text,
            ["mrkdwn"] = true
        };

        if (blocks is not null)
        {
            payload["blocks"] = blocks;
        }

        if (!string.IsNullOrEmpty(threadTs))
        {
            payload["thread_ts"] = threadTs;
        }

        req.Content = JsonContent.Create(payload);

        HttpResponseMessage res;
        string body;
        try
        {
            res = await _http.SendAsync(req, ct);
            body = await res.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HTTP exception calling chat.postMessage");
            return MessagingResult.Fail("http_exception", ex.ToString());
        }

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Slack chat.postMessage failed: {Status} {Body}", res.StatusCode, body);
            return MessagingResult.Fail($"http_{(int)res.StatusCode}", body, (int)res.StatusCode);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                if (root.TryGetProperty("ts", out var ts))
                {
                    return MessagingResult.Ok(ts.GetString());
                }
                return MessagingResult.Fail("no_timestamp", body, (int)res.StatusCode);
            }

            var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            _log.LogWarning("Slack chat.postMessage error: {Error}", error);
            return MessagingResult.Fail(error ?? "unknown", body, (int)res.StatusCode);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse chat.postMessage response");
            return MessagingResult.Fail("json_parse_error", $"{ex.Message}\n\nResponse: {body}", (int)res.StatusCode);
        }
    }

    public async Task PostEphemeralAsync(string channelId, string userId, string text, object[]? blocks = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            _log.LogWarning("Slack BotToken not configured, cannot send ephemeral");
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBase}/chat.postEphemeral");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var payload = new Dictionary<string, object>
        {
            ["channel"] = channelId,
            ["user"] = userId,
            ["text"] = text,
            ["mrkdwn"] = true
        };

        if (blocks is not null)
        {
            payload["blocks"] = blocks;
        }

        req.Content = JsonContent.Create(payload);

        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Slack chat.postEphemeral failed: {Status} {Body}", res.StatusCode, body);
        }
    }

    private async Task<(string? ChannelId, string? Error)> OpenConversationAsync(string userId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiBase}/conversations.open");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);

        var payload = new Dictionary<string, object>
        {
            ["users"] = userId
        };

        req.Content = JsonContent.Create(payload);

        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Slack conversations.open failed: {Status} {Body}", res.StatusCode, body);
            return (null, $"HTTP {res.StatusCode}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                if (root.TryGetProperty("channel", out var channel) &&
                    channel.TryGetProperty("id", out var id))
                {
                    return (id.GetString(), null);
                }
            }
            else
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                _log.LogWarning("Slack conversations.open error: {Error}", error);
                return (null, error);
            }
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse conversations.open response");
            return (null, "parse_error");
        }

        return (null, "no_channel_id");
    }

    private string? ParseTimestamp(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                if (root.TryGetProperty("ts", out var ts))
                {
                    var messageTs = ts.GetString();
                    _log.LogDebug("Message ts={Ts}", messageTs);
                    return messageTs;
                }
            }
            else
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                _log.LogWarning("Slack API error: {Error}", error);
            }
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse Slack response");
        }

        return null;
    }
}
