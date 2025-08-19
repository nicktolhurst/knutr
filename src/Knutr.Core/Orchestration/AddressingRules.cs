namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;

public sealed record AddressingRules(
    string BotDisplayName,
    string BotUserId,
    string[] Aliases,
    bool ReplyInDMs,
    bool ReplyOnTag
)
{
    public bool ShouldRespond(MessageContext m)
    {
        // Slack DM channels start with D, reply in DMs if configured to do so.
        var isDm = m.ChannelId.StartsWith('D'); 
        if (isDm && ReplyInDMs) return true;

        // If the message is a direct mention of the bot or any of its aliases, respond if configured to do so.
        if (ReplyOnTag && (m.Text.Contains($"@{BotDisplayName}", StringComparison.OrdinalIgnoreCase) ||
                           Aliases.Any(a => m.Text.Contains(a, StringComparison.OrdinalIgnoreCase))))
            return true;
            
        return false;
    }
}
