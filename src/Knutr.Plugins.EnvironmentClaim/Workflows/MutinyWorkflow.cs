namespace Knutr.Plugins.EnvironmentClaim.Workflows;

using Knutr.Abstractions.Workflows;
using Knutr.Plugins.EnvironmentClaim.Messaging;
using Microsoft.Extensions.Logging;

/// <summary>
/// Workflow for forcefully taking over an environment claim.
/// All interactions happen via DM to keep channels clean.
///
/// Flow:
/// 1. User runs /knutr mutiny [env]
/// 2. User gets DM with confirmation buttons: [Yes, take over] [Cancel]
/// 3a. If confirmed: Button updates, take over, DM old owner, DM user with confirmation
/// 3b. If cancelled: Button updates to show cancelled
/// </summary>
public sealed class MutinyWorkflow : IWorkflow
{
    public string Name => "claim:mutiny";

    private readonly IClaimStore _store;
    private readonly ILogger<MutinyWorkflow> _log;

    public MutinyWorkflow(IClaimStore store, ILogger<MutinyWorkflow> log)
    {
        _store = store;
        _log = log;
    }

    public async Task<WorkflowResult> ExecuteAsync(IWorkflowContext context)
    {
        var environment = context.Get<string>("environment");
        var mutineerId = context.UserId;

        if (string.IsNullOrEmpty(environment))
        {
            return WorkflowResult.Fail("Missing environment parameter");
        }

        var claim = _store.Get(environment);
        if (claim is null)
        {
            return WorkflowResult.Fail($"Environment `{environment}` is not claimed. Use `/knutr claim {environment}` instead.");
        }

        if (claim.UserId == mutineerId)
        {
            return WorkflowResult.Fail("You already own this environment!");
        }

        var ownerId = claim.UserId;

        // ─────────────────────────────────────────────────────────────────
        // Step 1: DM the user with confirmation buttons
        // ─────────────────────────────────────────────────────────────────
        var yesActionId = context.GenerateButtonActionId("confirm");
        var noActionId = context.GenerateButtonActionId("cancel");

        var (confirmText, confirmBlocks) = ClaimBlocks.MutinyConfirmPrompt(
            environment, ownerId, yesActionId, noActionId);

        var dmResult = await context.TrySendDmAsync(mutineerId, confirmText, confirmBlocks);

        if (!dmResult.Success)
        {
            _log.LogWarning("Failed to send confirmation DM to {User} for mutiny: {Error} - {Detail}",
                mutineerId, dmResult.Error, dmResult.ErrorDetail);

            return WorkflowResult.Fail($"Could not send DM: {dmResult.Error}");
        }

        // ─────────────────────────────────────────────────────────────────
        // Step 2: Wait for button click
        // ─────────────────────────────────────────────────────────────────
        try
        {
            var action = await context.WaitForButtonClickAsync(timeout: TimeSpan.FromMinutes(2));

            if (action != "confirm")
            {
                // ─────────────────────────────────────────────────────────
                // Cancelled
                // ─────────────────────────────────────────────────────────
                await context.UpdateButtonMessageAsync(":x: Takeover cancelled.");

                return WorkflowResult.Ok("Takeover cancelled");
            }

            // ─────────────────────────────────────────────────────────────
            // Step 3: Execute the takeover
            // ─────────────────────────────────────────────────────────────
            var released = _store.Release(environment, ownerId, force: true);
            if (!released)
            {
                await context.UpdateButtonMessageAsync(":warning: Takeover failed - environment may have already been released.");
                return WorkflowResult.Fail("Failed to release claim. It may have already been released.");
            }

            var claimResult = _store.TryClaim(environment, mutineerId, $"Taken over from <@{ownerId}>");
            if (!claimResult.Success)
            {
                await context.UpdateButtonMessageAsync($":warning: Takeover failed: {claimResult.ErrorMessage}");
                return WorkflowResult.Fail($"Failed to claim after takeover: {claimResult.ErrorMessage}");
            }

            _log.LogWarning("MUTINY: User {Mutineer} took {Env} from {Owner}",
                mutineerId, environment, ownerId);

            // ─────────────────────────────────────────────────────────────
            // Step 4: Update button and notify parties
            // ─────────────────────────────────────────────────────────────

            // Update the button message
            await context.UpdateButtonMessageAsync(
                $":ship: You now control `{environment}`!");

            // DM the former owner
            await context.SendDmAsync(ownerId,
                $":crossed_swords: <@{mutineerId}> has taken over `{environment}`. Reach out to them if you still need it.");

            return WorkflowResult.Ok($"Takeover successful - {environment} transferred");
        }
        catch (TimeoutException)
        {
            // ─────────────────────────────────────────────────────────────
            // Timeout - treat as cancelled
            // ─────────────────────────────────────────────────────────────
            return WorkflowResult.Ok("Takeover timed out");
        }
    }
}
