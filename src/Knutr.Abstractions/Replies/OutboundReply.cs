namespace Knutr.Abstractions.Replies;

public enum ResponseMode { Exact, Rewrite, Free }

public sealed record OutboundReply(ReplyHandle Handle, Reply Payload, ResponseMode Mode);

public sealed record OutboundReaction(string ChannelId, string MessageTs, string Emoji);
