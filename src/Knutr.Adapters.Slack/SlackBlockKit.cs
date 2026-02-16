namespace Knutr.Adapters.Slack;

/// <summary>
/// Helper class for building Slack Block Kit structures.
/// </summary>
public static class SlackBlockKit
{
    /// <summary>
    /// Creates a confirmation message with approve/deny buttons.
    /// </summary>
    public static object[] ConfirmationBlocks(
        string actionId,
        string title,
        string description,
        string confirmButtonText = "Approve",
        string denyButtonText = "Cancel",
        string? payload = null)
    {
        return
        [
            new Dictionary<string, object>
            {
                ["type"] = "section",
                ["text"] = new Dictionary<string, object>
                {
                    ["type"] = "mrkdwn",
                    ["text"] = $"*{title}*\n{description}"
                }
            },
            new Dictionary<string, object>
            {
                ["type"] = "actions",
                ["block_id"] = $"confirm_{actionId}",
                ["elements"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "button",
                        ["text"] = new Dictionary<string, object>
                        {
                            ["type"] = "plain_text",
                            ["text"] = confirmButtonText
                        },
                        ["style"] = "primary",
                        ["action_id"] = $"{actionId}_approve",
                        ["value"] = payload ?? ""
                    },
                    new Dictionary<string, object>
                    {
                        ["type"] = "button",
                        ["text"] = new Dictionary<string, object>
                        {
                            ["type"] = "plain_text",
                            ["text"] = denyButtonText
                        },
                        ["style"] = "danger",
                        ["action_id"] = $"{actionId}_deny",
                        ["value"] = "cancel"
                    }
                }
            }
        ];
    }

    /// <summary>
    /// Creates a simple text section block.
    /// </summary>
    public static object TextSection(string text, bool markdown = true)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "section",
            ["text"] = new Dictionary<string, object>
            {
                ["type"] = markdown ? "mrkdwn" : "plain_text",
                ["text"] = text
            }
        };
    }

    /// <summary>
    /// Creates a divider block.
    /// </summary>
    public static object Divider()
    {
        return new Dictionary<string, object> { ["type"] = "divider" };
    }

    /// <summary>
    /// Creates a context block with small text.
    /// </summary>
    public static object Context(params string[] texts)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "context",
            ["elements"] = texts.Select(t => new Dictionary<string, object>
            {
                ["type"] = "mrkdwn",
                ["text"] = t
            }).ToArray()
        };
    }

    /// <summary>
    /// Creates blocks showing a completed action (replaces the confirmation buttons).
    /// </summary>
    public static object[] CompletedBlocks(string title, string status, bool success = true)
    {
        var emoji = success ? ":white_check_mark:" : ":x:";
        return
        [
            new Dictionary<string, object>
            {
                ["type"] = "section",
                ["text"] = new Dictionary<string, object>
                {
                    ["type"] = "mrkdwn",
                    ["text"] = $"{emoji} *{title}*\n{status}"
                }
            }
        ];
    }
}
