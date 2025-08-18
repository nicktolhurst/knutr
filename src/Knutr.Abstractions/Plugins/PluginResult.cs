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

    public static PluginResult SkipNl(Reply reply, ReplyOverrides? overrides = null)
        => new() { PassThrough = new(reply, overrides) };

    public static PluginResult AskNlFree(string? text = null, ReplyOverrides? overrides = null)
        => new() { AskNl = new(NlMode.Free, text, null, overrides) };

    public static PluginResult AskNlRewrite(string text, string? style = null, ReplyOverrides? overrides = null)
        => new() { AskNl = new(NlMode.Rewrite, text, style, overrides) };
}

public sealed record ReplyOverrides(ReplyTarget? Target = null, ReplyPolicy? Policy = null);
