namespace Knutr.Abstractions.Events;

public sealed record ChannelMembershipContext(
    string Adapter,
    string TeamId,
    string ChannelId,
    string UserId,
    bool Joined
);
