namespace Knutr.Abstractions.Events;

public sealed record CommandContext(
    string Adapter,
    string TeamId,
    string ChannelId,
    string UserId,
    string Command,
    string RawText,
    string? ResponseUrl = null,
    string? CorrelationId = null
);
