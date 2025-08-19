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
        switch (outb.Handle.Target)
        {
            case ResponseUrlTarget resp:
                await _http.PostAsJsonAsync(resp.ResponseUrl, new { text = outb.Payload.Text }, ct);
                break;
            case ThreadTarget th:
                await PostChatMessageAsync(th.ChannelId, outb.Payload.Text, th.ThreadTs, ct);
                break;
            case ChannelTarget ch:
                await PostChatMessageAsync(ch.ChannelId, outb.Payload.Text, null, ct);
                break;
            case DirectMessageTarget dm:
                // open IM then post (simplified: log-only unless BotToken provided)
                await PostDmAsync(dm.UserId, outb.Payload.Text, ct);
                break;
        }
    }

    private async Task PostChatMessageAsync(string channel, string text, string? threadTs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            log.LogInformation("Slack BotToken not configured, logging only: channel={Channel}, text={Text}", channel, text);
            return;
        }

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

        await PostChatMessageAsync(channelId, text, null, ct);
    }
}
