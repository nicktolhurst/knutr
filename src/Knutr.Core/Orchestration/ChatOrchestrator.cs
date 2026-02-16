namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.NL;
using Knutr.Core.Channels;
using Knutr.Core.Replies;
using Knutr.Core.Messaging;
using Knutr.Core.PluginServices;
using Microsoft.Extensions.Logging;

public sealed class ChatOrchestrator(
    CommandRouter router,
    AddressingRules rules,
    ChannelPolicy channelPolicy,
    INaturalLanguageEngine nl,
    IReplyService reply,
    RemotePluginDispatcher remoteDispatcher,
    IEventBus bus,
    ILogger<ChatOrchestrator> logger)
{
    public async Task OnCommandAsync(CommandContext ctx, CancellationToken ct = default)
    {
        if (!channelPolicy.IsChannelAllowed(ctx.ChannelId))
            return;

        if (router.TryRoute(ctx, out var handler, out var subcommand))
        {
            logger.LogInformation("Executing command {Command} subcommand {Subcommand} for user {UserId} in channel {ChannelId}",
                ctx.Command, subcommand ?? "none", ctx.UserId, ctx.ChannelId);

            var pr = await handler!(ctx);
            await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);
        }
        else
        {
            // Try remote plugin services before falling back to NL
            var remotePr = await remoteDispatcher.TryDispatchAsync(ctx, ct);
            if (remotePr is not null)
            {
                await HandlePluginResultAsync(remotePr, ReplyTargetFrom(ctx), ct);
            }
            else
            {
                logger.LogInformation("No command match for {Command}, falling back to NL", ctx.Command);
                var rep = await nl.GenerateAsync(NlMode.Free, ctx.RawText, null, ctx, ct);
                await reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
            }
        }
    }

    public async Task OnMessageAsync(MessageContext ctx, CancellationToken ct = default)
    {
        if (!channelPolicy.IsChannelAllowed(ctx.ChannelId))
            return;

        // Broadcast to remote plugin services that support scanning
        var scanResults = await remoteDispatcher.ScanAsync(ctx, ct);
        var suppressMention = false;

        // Scan replies always thread on the original message
        var scanTarget = new ThreadTarget(ctx.ChannelId, ctx.ThreadTs ?? ctx.MessageTs ?? ctx.ChannelId);

        foreach (var pr in scanResults)
        {
            if (pr.PassThrough is not null || pr.AskNl is not null)
                await HandlePluginResultAsync(pr, scanTarget, ct);
            if (pr.SuppressMention) suppressMention = true;
        }

        // Handle reactions from scan results
        foreach (var pr in scanResults.Where(r => r.Reactions is { Length: > 0 }))
            HandleReactions(pr);

        // First check for explicit command match
        if (router.TryRoute(ctx, out var handler, out _))
        {
            var pr = await handler!(ctx);
            await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);
            return;
        }

        // If the bot is mentioned, use NL fallback (unless suppressed by a scan plugin)
        if (!suppressMention && rules.ShouldRespond(ctx))
        {
            logger.LogDebug("Using NL fallback for user {UserId}", ctx.UserId);
            var rep = await nl.GenerateAsync(NlMode.Free, ctx.Text, null, ctx, ct);
            await reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
        }
    }

    private void HandleReactions(PluginResult pr)
    {
        if (pr.Reactions is null || pr.ReactToMessageTs is null || pr.ReactInChannelId is null)
            return;

        foreach (var emoji in pr.Reactions)
        {
            logger.LogInformation("Publishing reaction {Emoji} on message {Ts}", emoji, pr.ReactToMessageTs);
            bus.Publish(new OutboundReaction(pr.ReactInChannelId, pr.ReactToMessageTs, emoji));
        }
    }

    private static ReplyTarget ReplyTargetFrom(CommandContext ctx)
        => !string.IsNullOrWhiteSpace(ctx.ResponseUrl) ? new ResponseUrlTarget(ctx.ResponseUrl) : new ChannelTarget(ctx.ChannelId);

    private static ReplyTarget ReplyTargetFrom(MessageContext ctx)
        => !string.IsNullOrWhiteSpace(ctx.ResponseUrl) ? new ResponseUrlTarget(ctx.ResponseUrl)
            : !string.IsNullOrWhiteSpace(ctx.ThreadTs) ? new ThreadTarget(ctx.ChannelId, ctx.ThreadTs)
            : new ChannelTarget(ctx.ChannelId);

    private async Task HandlePluginResultAsync(PluginResult pr, ReplyTarget defaultTarget, CancellationToken ct)
    {
        ReplyPolicy ApplyUsername(ReplyPolicy? policy) =>
            pr.Username is not null
                ? (policy ?? new ReplyPolicy()) with { Username = pr.Username }
                : policy ?? new ReplyPolicy();

        if (pr.PassThrough is { } p)
        {
            var handle = new ReplyHandle(p.Overrides?.Target ?? defaultTarget, ApplyUsername(p.Overrides?.Policy));
            await reply.SendAsync(p.Reply, handle, ResponseMode.Exact, ct);
            return;
        }
        if (pr.AskNl is { } a)
        {
            logger.LogInformation("Sending to NLP (mode={Mode}): {Text}", a.Mode, a.Text);
            try
            {
                var rep = await nl.GenerateAsync(a.Mode, a.Text, a.Style, null, ct);
                logger.LogInformation("NLP response received: {Text}", rep.Text);
                var handle = new ReplyHandle(a.Overrides?.Target ?? defaultTarget, ApplyUsername(a.Overrides?.Policy));
                await reply.SendAsync(rep, handle, a.Mode == NlMode.Rewrite ? ResponseMode.Rewrite : ResponseMode.Free, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("LLM request failed: {Message}", ex.Message);
            }
            return;
        }
    }
}
