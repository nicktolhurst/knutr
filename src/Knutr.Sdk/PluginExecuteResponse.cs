namespace Knutr.Sdk;

/// <summary>
/// The response from a plugin service after executing a command.
/// Returned as JSON from POST /execute.
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
    /// Serialized as JSON objects.
    /// </summary>
    public object[]? Blocks { get; init; }

    /// <summary>
    /// If true, the response is only visible to the invoking user.
    /// </summary>
    public bool Ephemeral { get; init; }

    /// <summary>
    /// If true, the core should pass the text through the NL engine before replying.
    /// </summary>
    public bool UseNaturalLanguage { get; init; }

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
