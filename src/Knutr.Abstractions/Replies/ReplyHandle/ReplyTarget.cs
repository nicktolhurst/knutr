namespace Knutr.Abstractions.Replies;

public abstract record ReplyTarget;

public sealed record ResponseUrlTarget(string ResponseUrl) : ReplyTarget;
public sealed record ChannelTarget(string ChannelId) : ReplyTarget;
public sealed record ThreadTarget(string ChannelId, string ThreadTs) : ReplyTarget;
public sealed record DirectMessageTarget(string UserId) : ReplyTarget;
