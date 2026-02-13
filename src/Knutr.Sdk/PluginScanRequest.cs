namespace Knutr.Sdk;

/// <summary>
/// Sent from the core bot to plugin services that support scanning.
/// Every channel message is broadcast to scan-capable services via POST /scan.
/// Return null/empty text to indicate no match.
/// </summary>
public sealed class PluginScanRequest
{
    /// <summary>
    /// The full message text from the user.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Slack user ID of the sender.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Slack channel ID where the message was sent.
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Slack team/workspace ID.
    /// </summary>
    public string? TeamId { get; init; }

    /// <summary>
    /// Thread timestamp if the message is in a thread.
    /// </summary>
    public string? ThreadTs { get; init; }
}
