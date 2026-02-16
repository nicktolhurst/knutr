namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Messaging;
using Knutr.Abstractions.NL;
using Knutr.Abstractions.Plugins;
using Knutr.Core.PluginServices;
using Microsoft.Extensions.Logging;

public sealed class ReactionHandler(
    AddressingRules rules,
    IMessagingService messaging,
    RemotePluginDispatcher dispatcher,
    INaturalLanguageEngine nl,
    ILogger<ReactionHandler> logger)
{
    private const string TriggerEmoji = "knutr-teach-me";

    public async Task OnReactionAsync(ReactionContext ctx, CancellationToken ct = default)
    {
        if (ctx.Emoji != TriggerEmoji)
            return;

        // Ignore the bot's own reactions
        if (!string.IsNullOrWhiteSpace(rules.BotUserId) && ctx.UserId == rules.BotUserId)
            return;

        logger.LogInformation("Reaction {Emoji} from {User} on {ItemTs} in {Channel}",
            ctx.Emoji, ctx.UserId, ctx.ItemTs, ctx.ChannelId);

        var fetched = await messaging.FetchMessageAsync(ctx.ChannelId, ctx.ItemTs, ct);
        if (fetched is null)
        {
            logger.LogWarning("Could not fetch message for {ItemTs} in {Channel}", ctx.ItemTs, ctx.ChannelId);
            return;
        }

        // Create a synthetic MessageContext with a special ThreadTs to bypass JargonBuster dedup
        var syntheticCtx = new MessageContext(
            ctx.Adapter,
            ctx.TeamId,
            ctx.ChannelId,
            ctx.UserId,
            fetched.Text,
            ThreadTs: $"_reaction_{ctx.ItemTs}",
            MessageTs: ctx.ItemTs,
            CorrelationId: ctx.CorrelationId);

        var results = await dispatcher.ScanAsync(syntheticCtx, ct);

        // Post ephemeral in the thread if the reacted message is in a thread
        var threadTs = fetched.ThreadTs;

        foreach (var pr in results)
        {
            string? responseText = null;

            if (pr.AskNl is { } a)
            {
                logger.LogInformation("Sending to NLP (mode={Mode}): {Text}", a.Mode, a.Text);
                var reply = await nl.GenerateAsync(a.Mode, a.Text, a.Style, null, ct);
                logger.LogInformation("NLP response received: {Text}", reply.Text);
                responseText = reply.Text;
            }
            else if (pr.PassThrough is { } p)
            {
                responseText = p.Reply.Text;
            }

            if (string.IsNullOrWhiteSpace(responseText))
                continue;

            var blocks = BuildContextBlocks(responseText);
            await messaging.PostEphemeralAsync(ctx.ChannelId, ctx.UserId, responseText, blocks, threadTs, ct);
        }
    }

    private static object[] BuildContextBlocks(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.Select(line => (object)new Dictionary<string, object>
        {
            ["type"] = "context",
            ["elements"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "mrkdwn",
                    ["text"] = line
                }
            }
        }).ToArray();
    }
}
