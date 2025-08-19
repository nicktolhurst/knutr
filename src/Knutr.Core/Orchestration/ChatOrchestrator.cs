namespace Knutr.Core.Orchestration;

using System.Diagnostics;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.NL;
using Knutr.Core.Replies;
using Knutr.Core.Observability;

public sealed class ChatOrchestrator(
    CommandRouter router,
    AddressingRules rules,
    INaturalLanguageEngine nl,
    IReplyService reply,
    CoreMetrics metrics)
{
    private static readonly ActivitySource Activity = new("Knutr.Core");

    public async Task OnCommandAsync(CommandContext ctx, CancellationToken ct = default)
    {
        using var act = Activity.StartActivity("command");
        metrics.Messages.Add(1, [new KeyValuePair<string, object?>("type", "command")]);
        if (router.TryRoute(ctx, out var handler))
        {
            metrics.CommandsMatched.Add(1, [new KeyValuePair<string, object?>("command", ctx.Command)]);
            var pr = await handler!(ctx);
            await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);
        }
        else
        {
            // no registered command → NL fallback
            var rep = await nl.GenerateAsync(NlMode.Free, ctx.RawText, null, ctx, ct);
            await reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
        }
    }

    public async Task OnMessageAsync(MessageContext ctx, CancellationToken ct = default)
    {
        using var act = Activity.StartActivity("message");
        var sw = Stopwatch.StartNew();
        metrics.Messages.Add(1, [new KeyValuePair<string, object?>("type", "message")]);
        if (router.TryRoute(ctx, out var handler))
        {
            var pr = await handler!(ctx);
            await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);
        }
        else if (rules.ShouldRespond(ctx))
        {
            var rep = await nl.GenerateAsync(NlMode.Free, ctx.Text, null, ctx, ct);
            await reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
        }
        metrics.OrchestratorLatency.Record(sw.Elapsed.TotalMilliseconds);
    }

    private static ReplyTarget ReplyTargetFrom(CommandContext ctx)
        => ctx.ResponseUrl is { Length: >0 } ? new ResponseUrlTarget(ctx.ResponseUrl) : new ChannelTarget(ctx.ChannelId);

    private static ReplyTarget ReplyTargetFrom(MessageContext ctx)
        => !string.IsNullOrEmpty(ctx.ThreadTs) ? new ThreadTarget(ctx.ChannelId, ctx.ThreadTs) : new ChannelTarget(ctx.ChannelId);

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
            var rep = await nl.GenerateAsync(a.Mode, a.Text, a.Style, null, ct);
            var handle = new ReplyHandle(a.Overrides?.Target ?? defaultTarget, a.Overrides?.Policy ?? new ReplyPolicy());
            await reply.SendAsync(rep, handle, a.Mode == NlMode.Rewrite ? ResponseMode.Rewrite : ResponseMode.Free, ct);
            return;
        }
    }
}
