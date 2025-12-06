namespace Knutr.Core.Workflows;

using System.Collections.Concurrent;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.Workflows;
using Knutr.Core.Replies;

/// <summary>
/// Concrete implementation of IWorkflowContext.
/// </summary>
public sealed class WorkflowContext : IWorkflowContext
{
    private readonly ConcurrentDictionary<string, object> _state = new();
    private readonly IReplyService _replyService;
    private readonly IThreadedMessagingService _messagingService;
    private readonly CancellationTokenSource _cts;
    private TaskCompletionSource<string>? _inputTcs;
    private bool _threadEstablished;

    public WorkflowContext(
        string workflowId,
        string workflowName,
        CommandContext commandContext,
        IReplyService replyService,
        IThreadedMessagingService messagingService,
        IReadOnlyDictionary<string, object>? initialState = null)
    {
        WorkflowId = workflowId;
        WorkflowName = workflowName;
        CommandContext = commandContext;
        _replyService = replyService;
        _messagingService = messagingService;
        _cts = new CancellationTokenSource();

        if (initialState != null)
        {
            foreach (var kv in initialState)
                _state[kv.Key] = kv.Value;
        }
    }

    public string WorkflowId { get; }
    public string WorkflowName { get; }
    public WorkflowStatus Status { get; internal set; } = WorkflowStatus.Running;
    public CommandContext CommandContext { get; }
    public string UserId => CommandContext.UserId;
    public string ChannelId => CommandContext.ChannelId;
    public string? ThreadTs { get; internal set; }
    public CancellationToken CancellationToken => _cts.Token;

    public string? ErrorMessage { get; private set; }
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; internal set; }

    // ─────────────────────────────────────────────────────────────────
    // State Management
    // ─────────────────────────────────────────────────────────────────

    public T? Get<T>(string key)
    {
        if (_state.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    public void Set<T>(string key, T value) where T : notnull
        => _state[key] = value;

    public bool Has(string key) => _state.ContainsKey(key);

    public IReadOnlyDictionary<string, object> GetState()
        => new Dictionary<string, object>(_state);

    // ─────────────────────────────────────────────────────────────────
    // Messaging
    // ─────────────────────────────────────────────────────────────────

    public async Task SendAsync(string message, bool markdown = true)
    {
        CancellationToken.ThrowIfCancellationRequested();

        // For the first message, post directly to create a thread and capture the ts
        if (!_threadEstablished)
        {
            var ts = await _messagingService.PostMessageAsync(ChannelId, message, null, CancellationToken);
            if (!string.IsNullOrEmpty(ts))
            {
                ThreadTs = ts;
                _threadEstablished = true;
                return;
            }
            // Fall back to reply service if posting failed
        }

        // For subsequent messages, reply in the established thread
        var reply = new Reply(message, markdown);
        ReplyTarget target = !string.IsNullOrEmpty(ThreadTs)
            ? new ThreadTarget(ChannelId, ThreadTs)
            : new ChannelTarget(ChannelId);
        var handle = new ReplyHandle(target, new ReplyPolicy(Threading: ThreadingMode.ForceThread));

        await _replyService.SendAsync(reply, handle, ResponseMode.Exact, CancellationToken);
    }

    public async Task UpdateAsync(string messageTs, string newMessage, bool markdown = true)
    {
        CancellationToken.ThrowIfCancellationRequested();
        await _messagingService.UpdateMessageAsync(ChannelId, messageTs, newMessage, CancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────
    // User Interaction
    // ─────────────────────────────────────────────────────────────────

    public async Task<string> PromptAsync(string prompt, IEnumerable<string>? options = null, TimeSpan? timeout = null)
    {
        CancellationToken.ThrowIfCancellationRequested();

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);

        // Build prompt message with options
        var message = prompt;
        if (options != null)
        {
            var optionList = options.ToList();
            if (optionList.Count > 0)
            {
                message += "\n\nOptions: " + string.Join(", ", optionList.Select(o => $"`{o}`"));
            }
        }

        await SendAsync(message);

        // Set up waiting for input
        Status = WorkflowStatus.WaitingForInput;
        _inputTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, timeoutCts.Token);

            var completedTask = await Task.WhenAny(
                _inputTcs.Task,
                Task.Delay(Timeout.Infinite, linkedCts.Token));

            if (completedTask == _inputTcs.Task)
            {
                Status = WorkflowStatus.Running;
                return await _inputTcs.Task;
            }

            // Timeout or cancellation
            if (CancellationToken.IsCancellationRequested)
                throw new OperationCanceledException();

            throw new TimeoutException($"No response received within {effectiveTimeout.TotalMinutes} minutes");
        }
        finally
        {
            _inputTcs = null;
        }
    }

    public async Task<bool> ConfirmAsync(string prompt, TimeSpan? timeout = null)
    {
        var response = await PromptAsync(prompt + " (yes/no)", ["yes", "no"], timeout);
        return response.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || response.Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Provide input to a workflow waiting for user response.
    /// </summary>
    internal bool TryProvideInput(string input)
    {
        return _inputTcs?.TrySetResult(input) ?? false;
    }

    // ─────────────────────────────────────────────────────────────────
    // Waiting
    // ─────────────────────────────────────────────────────────────────

    public async Task DelayAsync(TimeSpan duration, string? progressMessage = null)
    {
        CancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(progressMessage))
        {
            await SendAsync(progressMessage);
        }

        Status = WorkflowStatus.WaitingForEvent;
        try
        {
            await Task.Delay(duration, CancellationToken);
        }
        finally
        {
            Status = WorkflowStatus.Running;
        }
    }

    public async Task<bool> WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan interval,
        TimeSpan timeout,
        string? progressMessage = null)
    {
        CancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(progressMessage))
        {
            await SendAsync(progressMessage);
        }

        Status = WorkflowStatus.WaitingForEvent;
        var startTime = DateTime.UtcNow;

        try
        {
            while (DateTime.UtcNow - startTime < timeout)
            {
                CancellationToken.ThrowIfCancellationRequested();

                if (await condition())
                {
                    return true;
                }

                await Task.Delay(interval, CancellationToken);
            }

            return false; // Timed out
        }
        finally
        {
            Status = WorkflowStatus.Running;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Control Flow
    // ─────────────────────────────────────────────────────────────────

    public void Fail(string errorMessage)
    {
        ErrorMessage = errorMessage;
        Status = WorkflowStatus.Failed;
        _cts.Cancel();
    }

    public void Cancel(string? reason = null)
    {
        ErrorMessage = reason ?? "Cancelled by user";
        Status = WorkflowStatus.Cancelled;
        _cts.Cancel();
        _inputTcs?.TrySetCanceled();
    }

}
