namespace Knutr.Core.Workflows;

/// <summary>
/// Service for posting messages that support threading.
/// The first message posted to a channel creates a thread, and subsequent
/// messages can reply in that thread using the returned timestamp.
/// </summary>
public interface IThreadedMessagingService
{
    /// <summary>
    /// Posts a message to a channel and returns the message timestamp.
    /// </summary>
    /// <param name="channelId">The channel to post to.</param>
    /// <param name="text">The message text.</param>
    /// <param name="threadTs">Optional thread timestamp to reply in an existing thread.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The timestamp of the posted message, which can be used as thread_ts for replies.</returns>
    Task<string?> PostMessageAsync(string channelId, string text, string? threadTs = null, CancellationToken ct = default);

    /// <summary>
    /// Posts a message with Block Kit blocks for rich formatting.
    /// </summary>
    /// <param name="channelId">The channel to post to.</param>
    /// <param name="text">Fallback text for notifications.</param>
    /// <param name="blocks">Block Kit blocks array.</param>
    /// <param name="threadTs">Optional thread timestamp to reply in an existing thread.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The timestamp of the posted message.</returns>
    Task<string?> PostBlocksAsync(string channelId, string text, object[] blocks, string? threadTs = null, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing message with new text.
    /// </summary>
    /// <param name="channelId">The channel containing the message.</param>
    /// <param name="messageTs">The timestamp of the message to update.</param>
    /// <param name="newText">The new message text.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateMessageAsync(string channelId, string messageTs, string newText, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing message with Block Kit blocks.
    /// </summary>
    /// <param name="channelId">The channel containing the message.</param>
    /// <param name="messageTs">The timestamp of the message to update.</param>
    /// <param name="text">Fallback text for notifications.</param>
    /// <param name="blocks">Block Kit blocks array.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateBlocksAsync(string channelId, string messageTs, string text, object[] blocks, CancellationToken ct = default);
}
