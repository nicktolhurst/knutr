namespace Knutr.Abstractions.Messaging;

/// <summary>
/// Result of a messaging operation with detailed error information.
/// </summary>
public sealed record MessagingResult
{
    public bool Success { get; init; }
    public string? MessageTs { get; init; }
    public string? Error { get; init; }
    public string? ErrorDetail { get; init; }
    public int? HttpStatus { get; init; }

    public static MessagingResult Ok(string? messageTs) => new() { Success = true, MessageTs = messageTs };
    public static MessagingResult Fail(string error, string? detail = null, int? httpStatus = null)
        => new() { Success = false, Error = error, ErrorDetail = detail, HttpStatus = httpStatus };

    /// <summary>
    /// Formats the error for display to users in a code block.
    /// </summary>
    public string FormatError()
    {
        if (Success) return string.Empty;

        var lines = new List<string> { $"Error: {Error ?? "Unknown error"}" };

        if (HttpStatus.HasValue)
            lines.Add($"HTTP Status: {HttpStatus}");

        if (!string.IsNullOrEmpty(ErrorDetail))
            lines.Add($"Detail: {ErrorDetail}");

        return string.Join("\n", lines);
    }
}

/// <summary>
/// Service for posting messages that support threading, DMs, and ephemeral messages.
/// </summary>
public interface IMessagingService
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

    /// <summary>
    /// Sends a direct message to a specific user.
    /// </summary>
    /// <param name="userId">The user ID to message.</param>
    /// <param name="text">The message text.</param>
    /// <param name="blocks">Optional Block Kit blocks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The timestamp of the posted message.</returns>
    Task<string?> PostDmAsync(string userId, string text, object[]? blocks = null, CancellationToken ct = default);

    /// <summary>
    /// Sends a direct message with detailed result information for error handling.
    /// </summary>
    /// <param name="userId">The user ID to message.</param>
    /// <param name="text">The message text.</param>
    /// <param name="blocks">Optional Block Kit blocks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing success/failure details and any error information.</returns>
    Task<MessagingResult> TryPostDmAsync(string userId, string text, object[]? blocks = null, CancellationToken ct = default);

    /// <summary>
    /// Sends an ephemeral message visible only to a specific user in a channel.
    /// </summary>
    /// <param name="channelId">The channel to post in.</param>
    /// <param name="userId">The user who will see the message.</param>
    /// <param name="text">The message text.</param>
    /// <param name="blocks">Optional Block Kit blocks.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PostEphemeralAsync(string channelId, string userId, string text, object[]? blocks = null, CancellationToken ct = default);
}
