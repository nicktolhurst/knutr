namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.NL;
using Knutr.Core.Replies;
using Knutr.Core.Messaging;
using Knutr.Core.PluginServices;
using Microsoft.Extensions.Logging;

public sealed class ChatOrchestrator(
    CommandRouter router,
    AddressingRules rules,
    INaturalLanguageEngine nl,
    IReplyService reply,
    RemotePluginDispatcher remoteDispatcher,
    IEventBus bus,
    ILogger<ChatOrchestrator> logger)
{
    public async Task OnCommandAsync(CommandContext ctx, CancellationToken ct = default)
    {
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
        // Broadcast to remote plugin services that support scanning
        var scanResults = await remoteDispatcher.ScanAsync(ctx, ct);
        var suppressMention = false;
        foreach (var pr in scanResults)
        {
            if (pr.PassThrough is not null || pr.AskNl is not null)
                await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);
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
        => ctx.ResponseUrl is { Length: >0 } ? new ResponseUrlTarget(ctx.ResponseUrl) : new ChannelTarget(ctx.ChannelId);

    private static ReplyTarget ReplyTargetFrom(MessageContext ctx)
        => ctx.ResponseUrl is { Length: > 0 } ? new ResponseUrlTarget(ctx.ResponseUrl)
            : !string.IsNullOrEmpty(ctx.ThreadTs) ? new ThreadTarget(ctx.ChannelId, ctx.ThreadTs)
            : new ChannelTarget(ctx.ChannelId);

    private async Task HandlePluginResultAsync(PluginResult pr, ReplyTarget defaultTarget, CancellationToken ct)
    {
        if (pr.PassThrough is { } p)
        {
            var handle = new ReplyHandle(p.Overrides?.Target ?? defaultTarget, p.Overrides?.Policy ?? new ReplyPolicy());
            await reply.SendAsync(p.Reply, handle, ResponseMode.Exact, ct);
            return;
        }
        if (pr.AskNl is { } a)
        {
            logger.LogInformation("Sending plugin content to LLM for NLP {Mode}", a.Mode);
            try
            {
                var rep = await nl.GenerateAsync(a.Mode, a.Text, a.Style, null, ct);
                var handle = new ReplyHandle(a.Overrides?.Target ?? defaultTarget, a.Overrides?.Policy ?? new ReplyPolicy());
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
