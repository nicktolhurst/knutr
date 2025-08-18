namespace Knutr.Abstractions.Replies;

public enum ThreadingMode { PreferThread, ForceThread, ForceChannel, ForceDirectMessage }

public sealed record ReplyPolicy(
    ThreadingMode Threading = ThreadingMode.PreferThread,
    bool AllowNewThread = true,
    bool Ephemeral = false,
    bool SuppressMentions = true
);
