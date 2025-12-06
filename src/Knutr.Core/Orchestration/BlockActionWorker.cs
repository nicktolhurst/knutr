namespace Knutr.Core.Orchestration;

using Knutr.Abstractions.Events;
using Knutr.Core.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background worker that handles block action events from Slack.
/// </summary>
public sealed class BlockActionWorker(
    IEventBus bus,
    IConfirmationService confirmations,
    ILogger<BlockActionWorker> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bus.Subscribe<BlockActionContext>(async (ctx, ct) =>
        {
            log.LogInformation("Handling block action: {ActionId}", ctx.ActionId);
            await confirmations.HandleActionAsync(ctx, ct);
        });

        return Task.CompletedTask;
    }
}
