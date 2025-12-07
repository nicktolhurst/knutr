namespace Knutr.Core.Workflows;

using Knutr.Abstractions.Events;

/// <summary>
/// Service for managing workflow button interactions.
/// Handles routing button clicks to waiting workflows.
/// </summary>
public interface IWorkflowButtonService
{
    /// <summary>
    /// Generate a unique action ID for a workflow button.
    /// Format: "wf_{workflowId}_{action}"
    /// </summary>
    string GenerateActionId(string workflowId, string action);

    /// <summary>
    /// Try to extract workflow ID and action from an action ID.
    /// </summary>
    bool TryGetWorkflowAction(string actionId, out string? workflowId, out string? action);

    /// <summary>
    /// Handle a button click event and route to the waiting workflow.
    /// </summary>
    Task HandleButtonClickAsync(BlockActionContext ctx, string workflowId, string action, CancellationToken ct = default);

    /// <summary>
    /// Update the original button message via response_url after a button is clicked.
    /// </summary>
    Task UpdateButtonMessageAsync(string responseUrl, string text, object[]? blocks = null, CancellationToken ct = default);
}
