namespace Knutr.Abstractions.Workflows;

/// <summary>
/// Represents a multi-step workflow that can be executed by the workflow engine.
/// </summary>
public interface IWorkflow
{
    /// <summary>
    /// Unique name for this workflow type.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Execute the workflow.
    /// Use the context to send messages, prompt users, wait for events, etc.
    /// </summary>
    /// <param name="context">The workflow execution context.</param>
    /// <returns>A result indicating success or failure with optional message.</returns>
    Task<WorkflowResult> ExecuteAsync(IWorkflowContext context);
}

/// <summary>
/// Result of a workflow execution.
/// </summary>
public sealed class WorkflowResult
{
    private WorkflowResult() { }

    /// <summary>Whether the workflow completed successfully.</summary>
    public bool Success { get; private init; }

    /// <summary>Optional message describing the result.</summary>
    public string? Message { get; private init; }

    /// <summary>Optional data returned from the workflow.</summary>
    public IReadOnlyDictionary<string, object>? Data { get; private init; }

    /// <summary>Create a successful result.</summary>
    public static WorkflowResult Ok(string? message = null, IReadOnlyDictionary<string, object>? data = null)
        => new() { Success = true, Message = message, Data = data };

    /// <summary>Create a failed result.</summary>
    public static WorkflowResult Fail(string message)
        => new() { Success = false, Message = message };

    /// <summary>Create a cancelled result.</summary>
    public static WorkflowResult Cancelled(string? reason = null)
        => new() { Success = false, Message = reason ?? "Workflow was cancelled" };
}
