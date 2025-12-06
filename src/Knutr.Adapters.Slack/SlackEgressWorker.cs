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
    private readonly SlackOptions opts = opts.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bus.Subscribe<OutboundReply>(async (msg, ct) =>
        {
            log.LogInformation("Egress: outbound reply (mode={Mode})", msg.Mode);
            await SendAsync(msg, ct);
        });
        return Task.CompletedTask;
    }

    private async Task SendAsync(OutboundReply outb, CancellationToken ct)
    {
        var isEphemeral = outb.Handle.Policy?.Ephemeral ?? false;

        switch (outb.Handle.Target)
        {
            case ResponseUrlTarget resp:
                // Slack response_url: ephemeral by default, "in_channel" makes it visible to all
                var responsePayload = new Dictionary<string, object>
                {
                    ["text"] = outb.Payload.Text,
                    ["response_type"] = isEphemeral ? "ephemeral" : "in_channel"
                };
                await _http.PostAsJsonAsync(resp.ResponseUrl, responsePayload, ct);
                break;
            case ThreadTarget th:
                await PostChatMessageAsync(th.ChannelId, outb.Payload.Text, th.ThreadTs, isEphemeral, ct);
                break;
            case ChannelTarget ch:
                await PostChatMessageAsync(ch.ChannelId, outb.Payload.Text, null, isEphemeral, ct);
                break;
            case DirectMessageTarget dm:
                // open IM then post (simplified: log-only unless BotToken provided)
                await PostDmAsync(dm.UserId, outb.Payload.Text, ct);
                break;
        }
    }

    private async Task PostChatMessageAsync(string channel, string text, string? threadTs, bool ephemeral, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: channel={Channel}, text={Text}", channel, text);
            return;
        }

        // Note: chat.postEphemeral requires a user parameter, which we don't have here
        // For channel/thread messages, we use chat.postMessage (visible to all)
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{opts.ApiBase}/chat.postMessage");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.BotToken);
        var payload = new Dictionary<string, object> { ["channel"] = channel, ["text"] = text };
        if (!string.IsNullOrEmpty(threadTs)) payload["thread_ts"] = threadTs;
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
        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: DM to {UserId}: {Text}", userId, text);
            return;
        }

        // conversations.open
        using var openReq = new HttpRequestMessage(HttpMethod.Post, $"{opts.ApiBase}/conversations.open");
        openReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.BotToken);
        openReq.Content = JsonContent.Create(new { users = userId });
        var openRes = await _http.SendAsync(openReq, ct);
        var openBody = await openRes.Content.ReadAsStringAsync(ct);
        var channelId = userId;
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(openBody).RootElement;
            channelId = json.GetProperty("channel").GetProperty("id").GetString() ?? userId;
        } catch { }

        await PostChatMessageAsync(channelId, text, null, false, ct);
    }

    public async Task<string?> PostMessageAsync(string channel, string text, string? threadTs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: channel={Channel}, text={Text}", channel, text);
            return null;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{opts.ApiBase}/chat.postMessage");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.BotToken);
        var payload = new Dictionary<string, object> { ["channel"] = channel, ["text"] = text };
        if (!string.IsNullOrEmpty(threadTs)) payload["thread_ts"] = threadTs;
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
        catch { return null; }
    }

    public async Task UpdateMessageAsync(string channel, string ts, string text, object? blocks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: update channel={Channel}, ts={Ts}", channel, ts);
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{opts.ApiBase}/chat.update");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.BotToken);
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

    public async Task PostEphemeralAsync(string channel, string userId, string text, object? blocks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: ephemeral to {UserId} in {Channel}", userId, channel);
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{opts.ApiBase}/chat.postEphemeral");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.BotToken);
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
