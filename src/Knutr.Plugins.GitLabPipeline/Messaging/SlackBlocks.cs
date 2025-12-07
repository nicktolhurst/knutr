namespace Knutr.Plugins.GitLabPipeline.Messaging;

/// <summary>
/// Helper for building Slack Block Kit messages.
/// </summary>
public static class SlackBlocks
{
    /// <summary>Creates a section block with markdown text.</summary>
    public static object Section(string text) => new
    {
        type = "section",
        text = new { type = "mrkdwn", text }
    };

    /// <summary>Creates a section block with markdown text and an accessory button.</summary>
    public static object SectionWithButton(string text, string buttonText, string url) => new
    {
        type = "section",
        text = new { type = "mrkdwn", text },
        accessory = new
        {
            type = "button",
            text = new { type = "plain_text", text = buttonText, emoji = true },
            url
        }
    };

    /// <summary>Creates a context block with muted text elements.</summary>
    public static object Context(params string[] elements) => new
    {
        type = "context",
        elements = elements.Select(e => new { type = "mrkdwn", text = e }).ToArray()
    };

    /// <summary>Creates a divider block.</summary>
    public static object Divider() => new { type = "divider" };

    /// <summary>Creates a header block.</summary>
    public static object Header(string text) => new
    {
        type = "header",
        text = new { type = "plain_text", text, emoji = true }
    };
}
