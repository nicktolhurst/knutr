namespace Knutr.Abstractions.Replies;

/// <summary>
/// A reply message to send to the user.
/// </summary>
/// <param name="Text">The message text (also serves as fallback for Block Kit).</param>
/// <param name="Markdown">Whether the text uses Slack markdown formatting.</param>
/// <param name="Blocks">Optional Block Kit blocks for rich formatting.</param>
public sealed record Reply(string Text, bool Markdown = false, object[]? Blocks = null);
