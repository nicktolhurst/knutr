namespace Knutr.Abstractions.Events;

public sealed record MessageContext(
    string Adapter,
    string TeamId,
    string ChannelId,
    string UserId,
    string Text,
    string? ThreadTs = null,
    string? CorrelationId = null
);
