namespace Knutr.Plugins.EnvironmentClaim.Workflows;

using Knutr.Abstractions.Workflows;
using Knutr.Plugins.EnvironmentClaim.Messaging;
using Microsoft.Extensions.Logging;

/// <summary>
/// Workflow that sends a DM to the claim owner asking them to release.
/// Uses interactive buttons for yes/no response.
///
/// Flow:
/// 1. Requester runs /knutr nudge [env]
/// 2. Requester gets DM: "I've sent a message to @owner asking about [env]"
/// 3. Owner gets DM with buttons: "@requester is asking... [Yes, release it] [No, still using]"
/// 4a. If owner clicks Yes: Button updates, release happens, requester gets DM
/// 4b. If owner clicks No: Button updates, requester gets DM
/// 4c. If timeout: Requester gets DM that owner didn't respond
/// </summary>
public sealed class NudgeWorkflow : IWorkflow
{
    public string Name => "claim:nudge";

    private readonly IClaimStore _store;
    private readonly ILogger<NudgeWorkflow> _log;

    public NudgeWorkflow(IClaimStore store, ILogger<NudgeWorkflow> log)
    {
        _store = store;
        _log = log;
    }

    public async Task<WorkflowResult> ExecuteAsync(IWorkflowContext context)
    {
        var environment = context.Get<string>("environment");
        var requesterId = context.Get<string>("requester_id") ?? context.UserId;

        if (string.IsNullOrEmpty(environment))
        {
            return WorkflowResult.Fail("Missing environment parameter");
        }

        var claim = _store.Get(environment);
        if (claim is null)
        {
            return WorkflowResult.Fail($"Environment `{environment}` is not claimed");
        }

        var ownerId = claim.UserId;

        // Record the nudge
        _store.RecordNudge(environment);

        // ─────────────────────────────────────────────────────────────────
        // Step 1: DM the requester to confirm nudge was sent
        // ─────────────────────────────────────────────────────────────────
        await context.SendDmAsync(requesterId,
            $":wave: I've sent a message to <@{ownerId}> asking if they're done with `{environment}`.");

        // ─────────────────────────────────────────────────────────────────
        // Step 2: DM the owner with buttons
        // ─────────────────────────────────────────────────────────────────
        var yesActionId = context.GenerateButtonActionId("release");
        var noActionId = context.GenerateButtonActionId("keep");

        var (dmText, dmBlocks) = ClaimBlocks.NudgeDmToOwner(
            environment, requesterId, claim.ClaimDuration, claim.Note,
            yesActionId, noActionId);

        var dmResult = await context.TrySendDmAsync(ownerId, dmText, dmBlocks);

        if (!dmResult.Success)
        {
            _log.LogWarning("Failed to send DM to {Owner} for nudge: {Error} - {Detail}",
                ownerId, dmResult.Error, dmResult.ErrorDetail);

            // DM the requester about the error
            await context.SendDmAsync(requesterId,
                $":x: Could not send a DM to <@{ownerId}>.\n\n```\n{dmResult.FormatError()}\n```");

            return WorkflowResult.Fail($"Could not send DM: {dmResult.Error}");
        }

        // ─────────────────────────────────────────────────────────────────
        // Step 3: Wait for button click
        // ─────────────────────────────────────────────────────────────────
        try
        {
            var action = await context.WaitForButtonClickAsync(timeout: TimeSpan.FromMinutes(30));

            if (action == "release")
            {
                // ─────────────────────────────────────────────────────────
                // Owner agreed to release
                // ─────────────────────────────────────────────────────────
                var released = _store.Release(environment, ownerId);

                // Update the button message to show what was clicked
                await context.UpdateButtonMessageAsync(
                    $":white_check_mark: You released `{environment}`.");

                if (released)
                {
                    _log.LogInformation("User {Owner} released {Env} after nudge from {Requester}",
                        ownerId, environment, requesterId);

                    // DM the requester
                    await context.SendDmAsync(requesterId,
                        $":tada: <@{ownerId}> released `{environment}`! Use `/knutr claim {environment}` to claim it.");

                    return WorkflowResult.Ok($"Environment {environment} released");
                }
                else
                {
                    return WorkflowResult.Fail("Release failed - environment may have already been released");
                }
            }
            else
            {
                // ─────────────────────────────────────────────────────────
                // Owner declined
                // ─────────────────────────────────────────────────────────
                _log.LogInformation("User {Owner} declined to release {Env} after nudge from {Requester}",
                    ownerId, environment, requesterId);

                // Update the button message to show what was clicked
                await context.UpdateButtonMessageAsync(
                    $":lock: You're keeping `{environment}`.");

                // DM the requester
                await context.SendDmAsync(requesterId,
                    $":no_entry: <@{ownerId}> is still using `{environment}`. Try again later or use `/knutr mutiny {environment}` to force takeover.");

                return WorkflowResult.Ok("Owner declined to release");
            }
        }
        catch (TimeoutException)
        {
            // ─────────────────────────────────────────────────────────────
            // No response from owner
            // ─────────────────────────────────────────────────────────────
            _log.LogInformation("Nudge for {Env} timed out - no response from {Owner}",
                environment, ownerId);

            await context.SendDmAsync(requesterId,
                $":clock3: <@{ownerId}> didn't respond about `{environment}`. Use `/knutr mutiny {environment}` to force takeover.");

            return WorkflowResult.Ok("Nudge timed out");
        }
    }
}
