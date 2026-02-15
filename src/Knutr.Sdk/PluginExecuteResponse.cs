namespace Knutr.Sdk;

/// <summary>
/// The response from a plugin service after executing a command.
/// Returned as JSON from POST /execute.
/// <para>
/// Response modes are mutually exclusive — the core processes the first matching mode:
/// 1. <c>Success=false</c> — error response, <see cref="Error"/> is sent to the user.
/// 2. <c>UseNaturalLanguage=true</c> — text is passed through the NL engine before replying.
/// 3. <c>Ephemeral=true</c> — text is sent as an ephemeral message (visible only to invoker).
/// 4. Default — text is sent as-is (pass-through).
/// </para>
/// <para>
/// Orthogonal flags (can combine with any mode):
/// <see cref="SuppressMention"/>, <see cref="Reactions"/>.
/// </para>
/// </summary>
public sealed class PluginExecuteResponse
{
    public bool Success { get; init; }

    /// <summary>
    /// The text response to send back to the user.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Whether the text is markdown-formatted.
    /// </summary>
    public bool Markdown { get; init; }

    /// <summary>
    /// Optional Slack Block Kit blocks for rich formatting.
    /// </summary>
    public object[]? Blocks { get; init; }

    /// <summary>
    /// If true, the response is only visible to the invoking user.
    /// Mutually exclusive with <see cref="UseNaturalLanguage"/>.
    /// </summary>
    public bool Ephemeral { get; init; }

    /// <summary>
    /// If true, the core should pass the text through the NL engine before replying.
    /// Mutually exclusive with <see cref="Ephemeral"/>.
    /// </summary>
    public bool UseNaturalLanguage { get; init; }

    /// <summary>
    /// Optional style/tone guidance for the NL engine when <see cref="UseNaturalLanguage"/> is true.
    /// When set, the core uses a directed rewrite instead of free-form generation.
    /// Ignored when <see cref="UseNaturalLanguage"/> is false.
    /// </summary>
    public string? NaturalLanguageStyle { get; init; }

    /// <summary>
    /// If true, core should skip further mention/NL processing for this message.
    /// Can be combined with any response mode.
    /// </summary>
    public bool SuppressMention { get; init; }

    /// <summary>
    /// Emoji names to react to the original message with (e.g., ["eyes", "thinking_face"]).
    /// Can be combined with any response mode.
    /// </summary>
    public string[]? Reactions { get; init; }

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? Error { get; init; }

    public static PluginExecuteResponse Ok(string text, bool markdown = false, object[]? blocks = null)
        => new() { Success = true, Text = text, Markdown = markdown, Blocks = blocks };

    public static PluginExecuteResponse EphemeralOk(string text, bool markdown = true)
        => new() { Success = true, Text = text, Markdown = markdown, Ephemeral = true };

    public static PluginExecuteResponse Fail(string error)
        => new() { Success = false, Error = error };
}
