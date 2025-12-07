namespace Knutr.Abstractions.Workflows;

using Knutr.Abstractions.Events;

/// <summary>
/// Engine for executing and managing workflows.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Start a new workflow execution.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow type to execute.</typeparam>
    /// <param name="commandContext">The command that triggered this workflow.</param>
    /// <param name="initialState">Optional initial state for the workflow.</param>
    /// <returns>The workflow ID.</returns>
    Task<string> StartAsync<TWorkflow>(
        CommandContext commandContext,
        IReadOnlyDictionary<string, object>? initialState = null)
        where TWorkflow : IWorkflow;

    /// <summary>
    /// Start a workflow by name.
    /// </summary>
    Task<string> StartAsync(
        string workflowName,
        CommandContext commandContext,
        IReadOnlyDictionary<string, object>? initialState = null);

    /// <summary>
    /// Resume a workflow that was waiting for input with the user's response.
    /// </summary>
    /// <param name="workflowId">The workflow to resume.</param>
    /// <param name="input">The user's input.</param>
    /// <param name="responseUrl">Optional response URL for updating the message that triggered the input.</param>
    /// <returns>True if workflow was found and resumed.</returns>
    Task<bool> ResumeWithInputAsync(string workflowId, string input, string? responseUrl = null);

    /// <summary>
    /// Cancel a running workflow.
    /// </summary>
    Task<bool> CancelAsync(string workflowId, string? reason = null);

    /// <summary>
    /// Get the status of a workflow.
    /// </summary>
    Task<WorkflowInfo?> GetWorkflowAsync(string workflowId);

    /// <summary>
    /// Get all active workflows for a user.
    /// </summary>
    Task<IReadOnlyList<WorkflowInfo>> GetActiveWorkflowsAsync(string userId);

    /// <summary>
    /// Try to find an active workflow waiting for input in the given channel/thread.
    /// </summary>
    Task<WorkflowInfo?> FindWaitingWorkflowAsync(string channelId, string? threadTs);
}

/// <summary>
/// Information about a workflow instance.
/// </summary>
public sealed record WorkflowInfo(
    string WorkflowId,
    string WorkflowName,
    WorkflowStatus Status,
    string UserId,
    string ChannelId,
    string? ThreadTs,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IReadOnlyDictionary<string, object> State);
