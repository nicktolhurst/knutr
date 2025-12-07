namespace Knutr.Abstractions.Workflows;

/// <summary>
/// Represents the current state of a workflow execution.
/// </summary>
public enum WorkflowStatus
{
    /// <summary>Workflow is actively executing.</summary>
    Running,

    /// <summary>Workflow is waiting for an external event (build completion, etc.).</summary>
    WaitingForEvent,

    /// <summary>Workflow is waiting for user input.</summary>
    WaitingForInput,

    /// <summary>Workflow completed successfully.</summary>
    Completed,

    /// <summary>Workflow failed with an error.</summary>
    Failed,

    /// <summary>Workflow was cancelled by user or system.</summary>
    Cancelled
}
