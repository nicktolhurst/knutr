namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Core.Messaging;
using Knutr.Core.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background worker that handles block action events from Slack.
/// Routes to either workflow engine (for workflow buttons) or confirmation service.
/// </summary>
public sealed class BlockActionWorker(
    IEventBus bus,
    IConfirmationService confirmations,
    IWorkflowButtonService workflowButtons,
    ILogger<BlockActionWorker> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bus.Subscribe<BlockActionContext>(async (ctx, ct) =>
        {
            log.LogInformation("Handling block action: {ActionId}", ctx.ActionId);

            // Check if this is a workflow button (format: "wf_{workflowId}_{action}")
            if (workflowButtons.TryGetWorkflowAction(ctx.ActionId, out var workflowId, out var actionValue))
            {
                log.LogInformation("Routing to workflow {WorkflowId} with action {Action}", workflowId, actionValue);
                await workflowButtons.HandleButtonClickAsync(ctx, workflowId!, actionValue!, ct);
            }
            else
            {
                // Fall back to confirmation service
                await confirmations.HandleActionAsync(ctx, ct);
            }
        });

        return Task.CompletedTask;
    }
}
