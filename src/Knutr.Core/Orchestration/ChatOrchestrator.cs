namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Intent;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.NL;
using Knutr.Core.Replies;
using Knutr.Core.PluginServices;
using Microsoft.Extensions.Logging;

public sealed class ChatOrchestrator(
    CommandRouter router,
    AddressingRules rules,
    INaturalLanguageEngine nl,
    IReplyService reply,
    IIntentRecognizer intentRecognizer,
    IConfirmationService confirmations,
    RemotePluginDispatcher remoteDispatcher,
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
        foreach (var pr in scanResults)
            await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);

        // First check for explicit command match
        if (router.TryRoute(ctx, out var handler, out _))
        {
            var pr = await handler!(ctx);
            await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);
            return;
        }

        // If the bot is mentioned, try intent recognition
        if (rules.ShouldRespond(ctx))
        {
            var cleanText = rules.ExtractTextWithoutMention(ctx.Text);
            var intent = await intentRecognizer.RecognizeAsync(cleanText, ct);

            if (intent.HasIntent)
            {
                logger.LogInformation("Recognized intent {Command}:{Action} for user {UserId}", intent.Command, intent.Action, ctx.UserId);
                await confirmations.RequestConfirmationAsync(ctx, intent, ct);
                return;
            }

            logger.LogDebug("No intent recognized, using NL fallback for user {UserId}", ctx.UserId);
            var rep = await nl.GenerateAsync(NlMode.Free, ctx.Text, null, ctx, ct);
            await reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
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
            var rep = await nl.GenerateAsync(a.Mode, a.Text, a.Style, null, ct);
            var handle = new ReplyHandle(a.Overrides?.Target ?? defaultTarget, a.Overrides?.Policy ?? new ReplyPolicy());
            await reply.SendAsync(rep, handle, a.Mode == NlMode.Rewrite ? ResponseMode.Rewrite : ResponseMode.Free, ct);
            return;
        }
    }
}
