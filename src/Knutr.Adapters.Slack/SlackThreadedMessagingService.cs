namespace Knutr.Adapters.Slack;

using System.Net.Http.Json;
using System.Text.Json;
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

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                if (root.TryGetProperty("ts", out var ts))
                {
                    var messageTs = ts.GetString();
                    _log.LogDebug("Posted message to {Channel}, ts={Ts}", channelId, messageTs);
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

    public async Task UpdateMessageAsync(string channelId, string messageTs, string newText, CancellationToken ct = default)
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
            ["text"] = newText,
            ["mrkdwn"] = true
        };

        req.Content = JsonContent.Create(payload);

        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Slack chat.update failed: {Status} {Body}", res.StatusCode, body);
        }
    }
}
