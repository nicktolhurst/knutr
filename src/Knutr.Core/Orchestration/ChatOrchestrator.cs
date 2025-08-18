namespace Knutr.Core.Orchestration;

using System.Diagnostics;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.NL;
using Knutr.Core.Replies;
using Knutr.Core.Observability;

public sealed class ChatOrchestrator
{
    private static readonly ActivitySource Activity = new("Knutr.Core");
    private readonly CommandRouter _router;
    private readonly AddressingRules _rules;
    private readonly INaturalLanguageEngine _nl;
    private readonly IReplyService _reply;
    private readonly CoreMetrics _metrics;

    public ChatOrchestrator(CommandRouter router, AddressingRules rules, INaturalLanguageEngine nl, IReplyService reply, CoreMetrics metrics)
    {
        _router = router; _rules = rules; _nl = nl; _reply = reply; _metrics = metrics;
    }

    public async Task OnCommandAsync(CommandContext ctx, CancellationToken ct = default)
    {
        using var act = Activity.StartActivity("command");
        _metrics.Messages.Add(1, [new KeyValuePair<string, object?>("type", "command")]);
        if (_router.TryRoute(ctx, out var handler))
        {
            _metrics.CommandsMatched.Add(1, [new KeyValuePair<string, object?>("command", ctx.Command)]);
            var pr = await handler!(ctx);
            await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);
        }
        else
        {
            // no registered command → NL fallback
            var rep = await _nl.GenerateAsync(NlMode.Free, ctx.RawText, null, ctx, ct);
            await _reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
        }
    }

    public async Task OnMessageAsync(MessageContext ctx, CancellationToken ct = default)
    {
        using var act = Activity.StartActivity("message");
        var sw = Stopwatch.StartNew();
        _metrics.Messages.Add(1, [new KeyValuePair<string, object?>("type", "message")]);
        if (_router.TryRoute(ctx, out var handler))
        {
            var pr = await handler!(ctx);
            await HandlePluginResultAsync(pr, ReplyTargetFrom(ctx), ct);
        }
        else if (_rules.ShouldRespond(ctx))
        {
            var rep = await _nl.GenerateAsync(NlMode.Free, ctx.Text, null, ctx, ct);
            await _reply.SendAsync(rep, new ReplyHandle(ReplyTargetFrom(ctx), new ReplyPolicy()), ResponseMode.Free, ct);
        }
        _metrics.OrchestratorLatency.Record(sw.Elapsed.TotalMilliseconds);
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
            await _reply.SendAsync(p.Reply, handle, ResponseMode.Exact, ct);
            return;
        }
        if (pr.AskNl is { } nl)
        {
            var rep = await _nl.GenerateAsync(nl.Mode, nl.Text, nl.Style, null, ct);
            var handle = new ReplyHandle(nl.Overrides?.Target ?? defaultTarget, nl.Overrides?.Policy ?? new ReplyPolicy());
            await _reply.SendAsync(rep, handle, nl.Mode == NlMode.Rewrite ? ResponseMode.Rewrite : ResponseMode.Free, ct);
            return;
        }
    }
}
