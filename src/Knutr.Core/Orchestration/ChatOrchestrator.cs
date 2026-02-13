namespace Knutr.Core.Orchestration;

using System.Diagnostics;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Intent;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.NL;
using Knutr.Core.Replies;
using Knutr.Core.Observability;
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
    CoreMetrics metrics,
    ILogger<ChatOrchestrator> logger)
{
    private static readonly ActivitySource Activity = new("Knutr.Core");

    public async Task OnCommandAsync(CommandContext ctx, CancellationToken ct = default)
    {
        using var act = Activity.StartActivity("command");
        act?.SetTag("channel", ctx.ChannelId);
        act?.SetTag("user", ctx.UserId);
        act?.SetTag("command", ctx.Command);

        var sw = Stopwatch.StartNew();
        var outcome = "success";

        try
        {
            metrics.RecordMessage("command", ctx.ChannelId);

            if (router.TryRoute(ctx, out var handler, out var subcommand))
            {
                metrics.RecordCommandMatched(ctx.Command, subcommand, ctx.ChannelId);
                if (subcommand != null)
                {
                    metrics.RecordSubcommandInvocation(subcommand, ctx.ChannelId, ctx.UserId);
                    act?.SetTag("subcommand", subcommand);
                }

                logger.LogInformation("Executing command {Command} subcommand {Subcommand} for user {UserId} in channel {ChannelId}",
                    ctx.Command, subcommand ?? "none", ctx.UserId, ctx.ChannelId);

                var pr = await handler!(ctx);
                await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ctx.ChannelId, ct);
            }
            else
            {
                // Try remote plugin services before falling back to NL
                var remotePr = await remoteDispatcher.TryDispatchAsync(ctx, ct);
                if (remotePr is not null)
                {
                    act?.SetTag("dispatch", "remote");
                    await HandlePluginResultAsync(remotePr, ReplyTargetFrom(ctx), ctx.ChannelId, ct);
                    metrics.RecordReply(ctx.ChannelId, "remote_plugin");
                }
                else
                {
                    logger.LogInformation("No command match for {Command}, falling back to NL", ctx.Command);
                    var rep = await nl.GenerateAsync(NlMode.Free, ctx.RawText, null, ctx, ct);
                    await reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
                    metrics.RecordReply(ctx.ChannelId, "nl_fallback");
                }
            }
        }
        catch (Exception ex)
        {
            outcome = "error";
            act?.SetStatus(ActivityStatusCode.Error, ex.Message);
            act?.SetTag("exception.type", ex.GetType().FullName);
            act?.SetTag("exception.message", ex.Message);
            metrics.RecordError("command", "orchestrator", ex.GetType().Name);
            logger.LogError(ex, "Error processing command {Command} for user {UserId}", ctx.Command, ctx.UserId);
            throw;
        }
        finally
        {
            metrics.RecordLatency(sw.Elapsed.TotalMilliseconds, "command", outcome);
        }
    }

    public async Task OnMessageAsync(MessageContext ctx, CancellationToken ct = default)
    {
        using var act = Activity.StartActivity("message");
        act?.SetTag("channel", ctx.ChannelId);
        act?.SetTag("user", ctx.UserId);

        var sw = Stopwatch.StartNew();
        var outcome = "success";

        try
        {
            metrics.RecordMessage("message", ctx.ChannelId);

            // First check for explicit command match
            if (router.TryRoute(ctx, out var handler, out var subcommand))
            {
                if (subcommand != null)
                {
                    metrics.RecordSubcommandInvocation(subcommand, ctx.ChannelId, ctx.UserId);
                    act?.SetTag("subcommand", subcommand);
                }

                var pr = await handler!(ctx);
                await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ctx.ChannelId, ct);
                return;
            }

            // If the bot is mentioned, try intent recognition
            if (rules.ShouldRespond(ctx))
            {
                var cleanText = rules.ExtractTextWithoutMention(ctx.Text);

                var intentSw = Stopwatch.StartNew();
                var intent = await intentRecognizer.RecognizeAsync(cleanText, ct);
                metrics.RecordIntentLatency(intentSw.Elapsed.TotalMilliseconds, intent.HasIntent);

                act?.SetTag("intent_recognized", intent.HasIntent);
                if (intent.HasIntent)
                {
                    act?.SetTag("intent.command", intent.Command);
                    act?.SetTag("intent.action", intent.Action);
                }

                if (intent.HasIntent)
                {
                    logger.LogInformation("Recognized intent {Command}:{Action} for user {UserId}", intent.Command, intent.Action, ctx.UserId);
                    await confirmations.RequestConfirmationAsync(ctx, intent, ct);
                    return;
                }

                logger.LogDebug("No intent recognized, using NL fallback for user {UserId}", ctx.UserId);
                var rep = await nl.GenerateAsync(NlMode.Free, ctx.Text, null, ctx, ct);
                await reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
                metrics.RecordReply(ctx.ChannelId, "nl_fallback");
            }
        }
        catch (Exception ex)
        {
            outcome = "error";
            act?.SetStatus(ActivityStatusCode.Error, ex.Message);
            act?.SetTag("exception.type", ex.GetType().FullName);
            act?.SetTag("exception.message", ex.Message);
            metrics.RecordError("message", "orchestrator", ex.GetType().Name);
            logger.LogError(ex, "Error processing message for user {UserId} in channel {ChannelId}", ctx.UserId, ctx.ChannelId);
            throw;
        }
        finally
        {
            metrics.RecordLatency(sw.Elapsed.TotalMilliseconds, "message", outcome);
        }
    }

    private static ReplyTarget ReplyTargetFrom(CommandContext ctx)
        => ctx.ResponseUrl is { Length: >0 } ? new ResponseUrlTarget(ctx.ResponseUrl) : new ChannelTarget(ctx.ChannelId);

    private static ReplyTarget ReplyTargetFrom(MessageContext ctx)
        => !string.IsNullOrEmpty(ctx.ThreadTs) ? new ThreadTarget(ctx.ChannelId, ctx.ThreadTs) : new ChannelTarget(ctx.ChannelId);

    private async Task HandlePluginResultAsync(PluginResult pr, ReplyTarget defaultTarget, string channelId, CancellationToken ct)
    {
        if (pr.PassThrough is { } p)
        {
            var handle = new ReplyHandle(p.Overrides?.Target ?? defaultTarget, p.Overrides?.Policy ?? new ReplyPolicy());
            await reply.SendAsync(p.Reply, handle, ResponseMode.Exact, ct);
            var replyType = p.Overrides?.Policy?.Ephemeral == true ? "ephemeral" : "in_channel";
            metrics.RecordReply(channelId, replyType);
            return;
        }
        if (pr.AskNl is { } a)
        {
            var rep = await nl.GenerateAsync(a.Mode, a.Text, a.Style, null, ct);
            var handle = new ReplyHandle(a.Overrides?.Target ?? defaultTarget, a.Overrides?.Policy ?? new ReplyPolicy());
            await reply.SendAsync(rep, handle, a.Mode == NlMode.Rewrite ? ResponseMode.Rewrite : ResponseMode.Free, ct);
            metrics.RecordReply(channelId, "nl_response");
            return;
        }
    }
}
