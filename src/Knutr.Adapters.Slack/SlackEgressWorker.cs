using System.Net.Http.Json;
using Knutr.Abstractions.Replies;
using Knutr.Core.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Adapters.Slack;

public sealed class SlackEgressWorker(
    IEventBus bus,
    IHttpClientFactory httpFactory,
    IOptions<SlackOptions> opts,
    ILogger<SlackEgressWorker> log) : BackgroundService
{
    private readonly HttpClient _http = httpFactory.CreateClient("slack");
    private readonly SlackOptions _opts = opts.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bus.Subscribe<OutboundReply>(async (msg, ct) =>
        {
            var isEphemeral = msg.Handle.Policy?.Ephemeral ?? false;
            var egressType = msg.Handle.Target switch
            {
                DirectMessageTarget => "dm",
                ThreadTarget => isEphemeral ? "ephemeral, thread" : "thread",
                ChannelTarget => isEphemeral ? "ephemeral, channel" : "channel",
                ResponseUrlTarget => isEphemeral ? "ephemeral, response-url" : "response-url",
                _ => "unknown"
            };
            log.LogInformation("Egress: outbound reply ({EgressType}, mode={Mode})",
                egressType, msg.Mode);
            try
            {
                await SendAsync(msg, ct);
                log.LogDebug("Egress: reply sent successfully");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Egress: failed to send reply ({EgressType}, mode={Mode})", egressType, msg.Mode);
            }
        });
        bus.Subscribe<OutboundReaction>(async (msg, ct) =>
        {
            log.LogInformation("Egress: outbound reaction ({Emoji} on {MessageTs})", msg.Emoji, msg.MessageTs);
            try
            {
                await AddReactionAsync(msg.ChannelId, msg.MessageTs, msg.Emoji, ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Egress: failed to add reaction {Emoji}", msg.Emoji);
            }
        });
        return Task.CompletedTask;
    }

    private async Task SendAsync(OutboundReply outb, CancellationToken ct)
    {
        var isEphemeral = outb.Handle.Policy?.Ephemeral ?? false;
        var username = outb.Handle.Policy?.Username;

        switch (outb.Handle.Target)
        {
            case ResponseUrlTarget resp:
                // Slack response_url: ephemeral by default, "in_channel" makes it visible to all
                var responsePayload = new Dictionary<string, object>
                {
                    ["text"] = outb.Payload.Text,
                    ["response_type"] = isEphemeral ? "ephemeral" : "in_channel"
                };
                if (outb.Payload.Blocks is not null)
                    responsePayload["blocks"] = outb.Payload.Blocks;
                await _http.PostAsJsonAsync(resp.ResponseUrl, responsePayload, ct);
                break;
            case ThreadTarget th:
                await PostChatMessageAsync(th.ChannelId, outb.Payload.Text, outb.Payload.Blocks, th.ThreadTs, isEphemeral, username, ct);
                break;
            case ChannelTarget ch:
                await PostChatMessageAsync(ch.ChannelId, outb.Payload.Text, outb.Payload.Blocks, null, isEphemeral, username, ct);
                break;
            case DirectMessageTarget dm:
                // open IM then post (simplified: log-only unless BotToken provided)
                await PostDmAsync(dm.UserId, outb.Payload.Text, ct);
                break;
        }
    }

    private async Task PostChatMessageAsync(string channel, string text, object[]? blocks, string? threadTs, bool ephemeral, string? username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: channel={ChannelId}, text={Text}", channel, text);
            return;
        }

        // Note: chat.postEphemeral requires a user parameter, which we don't have here
        // For channel/thread messages, we use chat.postMessage (visible to all)
        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.ChatPostMessageUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);
        var payload = new Dictionary<string, object> { ["channel"] = channel, ["text"] = text };
        if (!string.IsNullOrWhiteSpace(threadTs)) payload["thread_ts"] = threadTs;
        if (blocks is not null) payload["blocks"] = blocks;
        if (!string.IsNullOrWhiteSpace(username)) payload["username"] = username;
        req.Content = JsonContent.Create(payload);
        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            log.LogWarning("Slack chat.postMessage failed: {Status} {Body}", res.StatusCode, body);
        }
    }

    private async Task PostDmAsync(string userId, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: DM to {UserId}: {Text}", userId, text);
            return;
        }

        // conversations.open
        using var openReq = new HttpRequestMessage(HttpMethod.Post, _opts.ConversationsOpenUrl);
        openReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);
        openReq.Content = JsonContent.Create(new { users = userId });
        var openRes = await _http.SendAsync(openReq, ct);
        var openBody = await openRes.Content.ReadAsStringAsync(ct);
        var channelId = userId;
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(openBody).RootElement;
            channelId = json.GetProperty("channel").GetProperty("id").GetString() ?? userId;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to parse conversations.open response for user {UserId}", userId);
        }

        await PostChatMessageAsync(channelId, text, null, null, false, null, ct);
    }

    public async Task<string?> PostMessageAsync(string channel, string text, string? threadTs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: channel={ChannelId}, text={Text}", channel, text);
            return null;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.ChatPostMessageUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);
        var payload = new Dictionary<string, object> { ["channel"] = channel, ["text"] = text };
        if (!string.IsNullOrWhiteSpace(threadTs)) payload["thread_ts"] = threadTs;
        req.Content = JsonContent.Create(payload);
        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogWarning("Slack chat.postMessage failed: {Status} {Body}", res.StatusCode, body);
            return null;
        }
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(body).RootElement;
            return json.GetProperty("ts").GetString();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to parse chat.postMessage response for channel {ChannelId}", channel);
            return null;
        }
    }

    public async Task UpdateMessageAsync(string channel, string ts, string text, object? blocks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: update channel={ChannelId}, messageTs={MessageTs}", channel, ts);
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.ChatUpdateUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);
        var payload = new Dictionary<string, object> { ["channel"] = channel, ["ts"] = ts, ["text"] = text };
        if (blocks != null) payload["blocks"] = blocks;
        req.Content = JsonContent.Create(payload);
        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            log.LogWarning("Slack chat.update failed: {Status} {Body}", res.StatusCode, body);
        }
    }

    public async Task AddReactionAsync(string channel, string timestamp, string emoji, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: reaction {Emoji} on {MessageTs} in {ChannelId}", emoji, timestamp, channel);
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.ReactionsAddUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);
        var payload = new Dictionary<string, object>
        {
            ["channel"] = channel,
            ["name"] = emoji,
            ["timestamp"] = timestamp
        };
        req.Content = JsonContent.Create(payload);
        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            log.LogWarning("Slack reactions.add failed: {Status} {Body}", res.StatusCode, body);
        }
    }

    public async Task PostEphemeralAsync(string channel, string userId, string text, object? blocks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: ephemeral to {UserId} in {Channel}", userId, channel);
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.ChatPostEphemeralUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _opts.BotToken);
        var payload = new Dictionary<string, object> { ["channel"] = channel, ["user"] = userId, ["text"] = text };
        if (blocks != null) payload["blocks"] = blocks;
        req.Content = JsonContent.Create(payload);
        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            log.LogWarning("Slack chat.postEphemeral failed: {Status} {Body}", res.StatusCode, body);
        }
    }
}
