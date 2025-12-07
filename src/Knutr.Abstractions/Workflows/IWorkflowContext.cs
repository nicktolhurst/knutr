namespace Knutr.Abstractions.Workflows;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Messaging;

/// <summary>
/// Context for workflow execution providing messaging, state, and control flow capabilities.
/// </summary>
public interface IWorkflowContext
{
    /// <summary>Unique identifier for this workflow instance.</summary>
    string WorkflowId { get; }

    /// <summary>Name of the workflow type.</summary>
    string WorkflowName { get; }

    /// <summary>Current status of the workflow.</summary>
    WorkflowStatus Status { get; }

    /// <summary>The original command context that triggered this workflow.</summary>
    CommandContext CommandContext { get; }

    /// <summary>User who initiated the workflow.</summary>
    string UserId { get; }

    /// <summary>Channel where the workflow is running.</summary>
    string ChannelId { get; }

    /// <summary>Thread timestamp for threaded conversations.</summary>
    string? ThreadTs { get; }

    /// <summary>Cancellation token for the workflow.</summary>
    CancellationToken CancellationToken { get; }

    // ─────────────────────────────────────────────────────────────────
    // State Management
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Get a value from workflow state.</summary>
    T? Get<T>(string key);

    /// <summary>Set a value in workflow state.</summary>
    void Set<T>(string key, T value) where T : notnull;

    /// <summary>Check if a key exists in workflow state.</summary>
    bool Has(string key);

    /// <summary>Get all state as a dictionary.</summary>
    IReadOnlyDictionary<string, object> GetState();

    // ─────────────────────────────────────────────────────────────────
    // Messaging
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a message to the user.
    /// If no thread is established, posts to the channel and creates a thread.
    /// If a thread is established, posts as a reply in that thread.
    /// </summary>
    Task SendAsync(string message, bool markdown = true);

    /// <summary>
    /// Send a message with Block Kit blocks for rich formatting.
    /// If this is the first message, posts to the channel and establishes the thread.
    /// Subsequent calls post to the thread.
    /// </summary>
    /// <param name="text">Fallback text for notifications.</param>
    /// <param name="blocks">Block Kit blocks array.</param>
    /// <returns>The timestamp of the posted message (for updates).</returns>
    Task<string?> SendBlocksAsync(string text, object[] blocks);

    /// <summary>
    /// Update a previously sent message by its timestamp.
    /// </summary>
    Task UpdateAsync(string messageTs, string newMessage, bool markdown = true);

    /// <summary>
    /// Update a message with Block Kit blocks.
    /// </summary>
    /// <param name="messageTs">The timestamp of the message to update.</param>
    /// <param name="text">Fallback text for notifications.</param>
    /// <param name="blocks">Block Kit blocks array.</param>
    Task UpdateBlocksAsync(string messageTs, string text, object[] blocks);

    /// <summary>
    /// Send a direct message to a specific user.
    /// </summary>
    /// <param name="userId">The user ID to message.</param>
    /// <param name="text">The message text.</param>
    /// <param name="blocks">Optional Block Kit blocks.</param>
    /// <returns>The timestamp of the posted message.</returns>
    Task<string?> SendDmAsync(string userId, string text, object[]? blocks = null);

    /// <summary>
    /// Send a direct message with detailed result information for error handling.
    /// </summary>
    /// <param name="userId">The user ID to message.</param>
    /// <param name="text">The message text.</param>
    /// <param name="blocks">Optional Block Kit blocks.</param>
    /// <returns>A result containing success/failure details and any error information.</returns>
    Task<MessagingResult> TrySendDmAsync(string userId, string text, object[]? blocks = null);

    /// <summary>
    /// Send an ephemeral message visible only to the workflow initiator.
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <param name="blocks">Optional Block Kit blocks.</param>
    Task SendEphemeralAsync(string text, object[]? blocks = null);

    /// <summary>
    /// Send an ephemeral message to a specific user in the workflow channel.
    /// </summary>
    /// <param name="userId">The user who will see the message.</param>
    /// <param name="text">The message text.</param>
    /// <param name="blocks">Optional Block Kit blocks.</param>
    Task SendEphemeralToUserAsync(string userId, string text, object[]? blocks = null);

    // ─────────────────────────────────────────────────────────────────
    // User Interaction
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prompt the user with a question and wait for their response.
    /// </summary>
    /// <param name="prompt">The question to ask.</param>
    /// <param name="options">Optional list of valid options (shown as buttons if supported).</param>
    /// <param name="timeout">How long to wait for response. Default is 5 minutes.</param>
    /// <returns>The user's response text.</returns>
    Task<string> PromptAsync(string prompt, IEnumerable<string>? options = null, TimeSpan? timeout = null);

    /// <summary>
    /// Prompt the user with a yes/no confirmation.
    /// </summary>
    Task<bool> ConfirmAsync(string prompt, TimeSpan? timeout = null);

    /// <summary>
    /// Generate an action ID for a button that will route back to this workflow.
    /// The action ID embeds the workflow ID so button clicks can be routed correctly.
    /// </summary>
    /// <param name="action">The action name (e.g., "yes", "no", "confirm")</param>
    string GenerateButtonActionId(string action);

    /// <summary>
    /// Wait for a button click that will be routed back to this workflow.
    /// Returns the action value from the clicked button.
    /// After this returns, you can call UpdateButtonMessageAsync to update the clicked message.
    /// </summary>
    /// <param name="timeout">How long to wait for a button click.</param>
    Task<string> WaitForButtonClickAsync(TimeSpan? timeout = null);

    /// <summary>
    /// Updates the message containing the button that was just clicked.
    /// Must be called after WaitForButtonClickAsync returns.
    /// </summary>
    /// <param name="text">New text for the message.</param>
    /// <param name="blocks">Optional new blocks (if null, just shows text).</param>
    Task UpdateButtonMessageAsync(string text, object[]? blocks = null);

    // ─────────────────────────────────────────────────────────────────
    // Waiting
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wait for a specified duration, sending optional progress updates.
    /// </summary>
    Task DelayAsync(TimeSpan duration, string? progressMessage = null);

    /// <summary>
    /// Poll a condition until it returns true or timeout is reached.
    /// </summary>
    /// <param name="condition">Async function that returns true when condition is met.</param>
    /// <param name="interval">How often to check the condition.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="progressMessage">Optional message to send while waiting.</param>
    /// <returns>True if condition was met, false if timed out.</returns>
    Task<bool> WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan interval,
        TimeSpan timeout,
        string? progressMessage = null);

    // ─────────────────────────────────────────────────────────────────
    // Control Flow
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mark the workflow as failed with an error message.
    /// </summary>
    void Fail(string errorMessage);

    /// <summary>
    /// Cancel the workflow.
    /// </summary>
    void Cancel(string? reason = null);
}
