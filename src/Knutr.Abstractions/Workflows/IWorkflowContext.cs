namespace Knutr.Abstractions.Workflows;

using Knutr.Abstractions.Events;

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
    /// Send a progress message to the user.
    /// Messages are sent in a thread under the original command.
    /// </summary>
    Task SendAsync(string message, bool markdown = true);

    /// <summary>
    /// Update a previously sent message by its timestamp.
    /// </summary>
    Task UpdateAsync(string messageTs, string newMessage, bool markdown = true);

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
