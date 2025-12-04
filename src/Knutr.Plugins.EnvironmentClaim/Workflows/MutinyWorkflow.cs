namespace Knutr.Plugins.EnvironmentClaim.Workflows;

using Knutr.Abstractions.Workflows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Workflow for forcefully taking over an environment claim.
/// Requires confirmation and notifies the original owner.
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
            return WorkflowResult.Fail($"Environment `{environment}` is not claimed. Use `/claim {environment}` instead.");
        }

        if (claim.UserId == mutineerId)
        {
            return WorkflowResult.Fail("You already own this environment. No mutiny needed!");
        }

        var ownerId = claim.UserId;
        var durationText = FormatDuration(claim.ClaimDuration);

        // Show current claim status and ask for confirmation
        await context.SendAsync(
            $":pirate_flag: *Mutiny Request for `{environment}`*\n\n" +
            $"Current owner: <@{ownerId}>\n" +
            $"• *Claimed for:* {durationText}\n" +
            $"• *Times nudged:* {claim.NudgeCount}\n" +
            $"• *Status:* {claim.Status}\n" +
            (claim.Note is not null ? $"• *Note:* {claim.Note}\n" : "") +
            "\n:warning: *This will forcefully remove their claim.*");

        try
        {
            // Require explicit confirmation
            var confirmed = await context.ConfirmAsync(
                $"Force take `{environment}` from <@{ownerId}>?",
                timeout: TimeSpan.FromMinutes(2));

            if (!confirmed)
            {
                await context.SendAsync(":no_entry_sign: Mutiny cancelled.");
                return WorkflowResult.Ok("Mutiny cancelled by user");
            }

            // Perform the mutiny
            var released = _store.Release(environment, ownerId, force: true);
            if (!released)
            {
                return WorkflowResult.Fail("Failed to release claim. It may have already been released.");
            }

            var claimResult = _store.TryClaim(environment, mutineerId, $"Mutiny from <@{ownerId}>");
            if (!claimResult.Success)
            {
                return WorkflowResult.Fail($"Failed to claim after mutiny: {claimResult.ErrorMessage}");
            }

            _log.LogWarning("MUTINY: User {Mutineer} forcefully took {Env} from {Owner}",
                mutineerId, environment, ownerId);

            // Notify everyone
            await context.SendAsync(
                $":pirate_flag: *Mutiny Successful!*\n\n" +
                $"`{environment}` has been taken from <@{ownerId}> by <@{mutineerId}>.\n\n" +
                $"<@{ownerId}> - your claim has been overridden. " +
                $"Please coordinate with <@{mutineerId}> if you still need this environment.");

            return WorkflowResult.Ok($"Mutiny successful - {environment} transferred to {mutineerId}");
        }
        catch (TimeoutException)
        {
            await context.SendAsync(":clock3: Mutiny request timed out.");
            return WorkflowResult.Ok("Mutiny timed out");
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{(int)duration.TotalMinutes}m";
    }
}
