namespace Knutr.Plugins.EnvironmentClaim.Workflows;

using Knutr.Abstractions.Workflows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Workflow that sends a message to the claim owner asking them to release.
/// If they confirm, releases the environment automatically.
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
        var requesterId = context.Get<string>("requester_id");
        var requesterName = context.Get<string>("requester_name") ?? requesterId;

        if (string.IsNullOrEmpty(environment))
        {
            return WorkflowResult.Fail("Missing environment parameter");
        }

        var claim = _store.Get(environment);
        if (claim is null)
        {
            return WorkflowResult.Fail($"Environment `{environment}` is not claimed");
        }

        // Record the nudge
        _store.RecordNudge(environment);

        var ownerId = claim.UserId;
        var claimDuration = claim.ClaimDuration;
        var durationText = FormatDuration(claimDuration);

        // Send nudge message
        await context.SendAsync(
            $":wave: *Nudge Request*\n\n" +
            $"<@{requesterId}> is asking if you could release `{environment}`.\n\n" +
            $"• *Claimed for:* {durationText}\n" +
            $"• *Times nudged:* {claim.NudgeCount}\n" +
            (claim.Note is not null ? $"• *Note:* {claim.Note}\n" : "") +
            $"\n_Would you like to release this environment?_");

        try
        {
            // Ask owner if they want to release
            var shouldRelease = await context.ConfirmAsync(
                $"Release `{environment}`?",
                timeout: TimeSpan.FromMinutes(10));

            if (shouldRelease)
            {
                // Owner agreed to release
                var released = _store.Release(environment, ownerId);

                if (released)
                {
                    _log.LogInformation("User {Owner} released {Env} after nudge from {Requester}",
                        ownerId, environment, requesterId);

                    await context.SendAsync(
                        $":white_check_mark: <@{ownerId}> has released `{environment}`!\n\n" +
                        $"<@{requesterId}> - the environment is now available.");

                    return WorkflowResult.Ok($"Environment {environment} released");
                }
                else
                {
                    await context.SendAsync(
                        $":warning: Failed to release `{environment}`. It may have already been released.");

                    return WorkflowResult.Fail("Release failed");
                }
            }
            else
            {
                // Owner declined
                _log.LogInformation("User {Owner} declined to release {Env} after nudge from {Requester}",
                    ownerId, environment, requesterId);

                await context.SendAsync(
                    $":no_entry: <@{ownerId}> is still using `{environment}`.\n\n" +
                    $"<@{requesterId}> - you may need to wait or use `/mutiny {environment}` to force takeover.");

                return WorkflowResult.Ok("Owner declined to release");
            }
        }
        catch (TimeoutException)
        {
            // No response from owner
            _log.LogInformation("Nudge for {Env} timed out - no response from {Owner}",
                environment, ownerId);

            await context.SendAsync(
                $":clock3: No response from <@{ownerId}> within 10 minutes.\n\n" +
                $"<@{requesterId}> - try again later or use `/mutiny {environment}` to force takeover.");

            return WorkflowResult.Ok("Nudge timed out");
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays} day(s), {duration.Hours} hour(s)";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours} hour(s), {duration.Minutes} minute(s)";
        return $"{(int)duration.TotalMinutes} minute(s)";
    }
}
