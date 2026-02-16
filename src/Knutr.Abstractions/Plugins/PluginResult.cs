namespace Knutr.Abstractions.Plugins;

using Knutr.Abstractions.Replies;

public enum NlMode { Free, Rewrite }

public sealed class PluginResult
{
    private PluginResult() { }

    public sealed record PassthroughResult(Reply Reply, ReplyOverrides? Overrides);
    public sealed record AskNlResult(NlMode Mode, string? Text, string? Style, ReplyOverrides? Overrides);

    public PassthroughResult? PassThrough { get; init; }
    public AskNlResult? AskNl { get; init; }
    public bool SuppressMention { get; set; }
    public string[]? Reactions { get; set; }
    public string? ReactToMessageTs { get; set; }
    public string? ReactInChannelId { get; set; }
    public string? Username { get; set; }

    public static PluginResult Empty() => new();

    public static PluginResult SkipNl(Reply reply, ReplyOverrides? overrides = null)
        => new() { PassThrough = new(reply, overrides) };

    /// <summary>
    /// Returns an ephemeral response (only visible to the user who ran the command).
    /// </summary>
    public static PluginResult Ephemeral(Reply reply)
        => new() { PassThrough = new(reply, new ReplyOverrides(Policy: new ReplyPolicy(Ephemeral: true))) };

    /// <summary>
    /// Returns an ephemeral response with just text.
    /// </summary>
    public static PluginResult Ephemeral(string text, bool markdown = true)
        => Ephemeral(new Reply(text, markdown));

    /// <summary>
    /// Returns an ephemeral response with blocks.
    /// </summary>
    public static PluginResult Ephemeral(string text, object[] blocks)
        => Ephemeral(new Reply(text, Markdown: true, Blocks: blocks));

    public static PluginResult AskNlFree(string? text = null, ReplyOverrides? overrides = null)
        => new() { AskNl = new(NlMode.Free, text, null, overrides) };

    public static PluginResult AskNlRewrite(string text, string? style = null, ReplyOverrides? overrides = null)
        => new() { AskNl = new(NlMode.Rewrite, text, style, overrides) };
}

public sealed record ReplyOverrides(ReplyTarget? Target = null, ReplyPolicy? Policy = null);
