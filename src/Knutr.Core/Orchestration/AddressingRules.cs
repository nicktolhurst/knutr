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
        // Never respond to messages from ourselves
        if (!string.IsNullOrWhiteSpace(BotUserId) && m.UserId == BotUserId)
            return false;

        // Never respond to empty messages or messages with no user
        if (string.IsNullOrWhiteSpace(m.Text) || string.IsNullOrWhiteSpace(m.UserId))
            return false;

        // Slack DM channels start with D, reply in DMs if configured to do so.
        var isDm = m.ChannelId.StartsWith('D');
        if (isDm && ReplyInDMs) return true;

        if (!ReplyOnTag) return false;

        // Slack mentions come through as <@USER_ID> in the text
        if (!string.IsNullOrWhiteSpace(BotUserId) && m.Text.Contains($"<@{BotUserId}>", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check display name mention (e.g., @knutr)
        if (m.Text.Contains($"@{BotDisplayName}", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check aliases
        if (Aliases.Any(a => m.Text.Contains(a, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    /// <summary>
    /// Extracts the text after removing the bot mention prefix.
    /// </summary>
    public string ExtractTextWithoutMention(string text)
    {
        // Remove Slack-style mentions like <@U123456>
        if (!string.IsNullOrWhiteSpace(BotUserId))
        {
            text = System.Text.RegularExpressions.Regex.Replace(
                text, $@"<@{BotUserId}>\s*,?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Remove @displayname mentions
        text = System.Text.RegularExpressions.Regex.Replace(
            text, $@"@{BotDisplayName}\s*,?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove alias mentions
        foreach (var alias in Aliases)
        {
            text = System.Text.RegularExpressions.Regex.Replace(
                text, $@"{System.Text.RegularExpressions.Regex.Escape(alias)}\s*,?\s*", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return text.Trim();
    }
}
