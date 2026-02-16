namespace Knutr.Abstractions.Events;

/// <summary>
/// Context for Slack block action interactions (button clicks, select menus, etc.)
/// </summary>
public sealed record BlockActionContext(
    string Adapter,
    string TeamId,
    string ChannelId,
    string UserId,
    string ActionId,
    string? ActionValue,
    string? BlockId,
    string ResponseUrl,
    string TriggerId,
    string? MessageTs = null
);
