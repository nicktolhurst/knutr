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
        var isDm = m.ChannelId.StartsWith("D"); // Slack DM channels start with D
        if (isDm && ReplyInDMs) return true;
        if (ReplyOnTag && (m.Text.Contains($"@{BotDisplayName}", StringComparison.OrdinalIgnoreCase) ||
                           Aliases.Any(a => m.Text.Contains(a, StringComparison.OrdinalIgnoreCase))))
            return true;
        return false;
    }
}
