namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Intent;

/// <summary>
/// Service for handling confirmation flows for intent-based actions.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Shows an ephemeral confirmation message for the given intent.
    /// </summary>
    Task RequestConfirmationAsync(MessageContext ctx, IntentResult intent, CancellationToken ct = default);

    /// <summary>
    /// Handles a block action (approve/deny) callback.
    /// </summary>
    Task HandleActionAsync(BlockActionContext ctx, CancellationToken ct = default);
}
