namespace Knutr.Abstractions.Events;

public sealed record ReactionContext(
    string Adapter,
    string TeamId,
    string ChannelId,
    string UserId,
    string Emoji,
    string ItemUserId,
    string ItemTs,
    string? CorrelationId = null
);
