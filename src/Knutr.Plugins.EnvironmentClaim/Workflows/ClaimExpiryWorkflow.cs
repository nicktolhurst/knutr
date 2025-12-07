namespace Knutr.Plugins.EnvironmentClaim.Workflows;

using Knutr.Abstractions.Workflows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background workflow that periodically checks for stale claims
/// and prompts owners to confirm they still need them.
/// </summary>
public sealed class ClaimExpiryWorkflow : IWorkflow
{
    public string Name => "claim:expiry-check";

    private readonly IClaimStore _store;
    private readonly ILogger<ClaimExpiryWorkflow> _log;

    // Default threshold for stale claims
    public static readonly TimeSpan DefaultStaleThreshold = TimeSpan.FromDays(2);

    public ClaimExpiryWorkflow(IClaimStore store, ILogger<ClaimExpiryWorkflow> log)
    {
        _store = store;
        _log = log;
    }

    public async Task<WorkflowResult> ExecuteAsync(IWorkflowContext context)
    {
        // Can be triggered for a specific environment or as a sweep
        var targetEnvironment = context.Get<string>("environment");
        var thresholdDays = context.Get<int?>("threshold_days") ?? 2;
        var threshold = TimeSpan.FromDays(thresholdDays);

        if (!string.IsNullOrEmpty(targetEnvironment))
        {
            // Check specific environment
            return await CheckSingleClaim(context, targetEnvironment, threshold);
        }

        // Sweep all stale claims
        return await SweepStaleClaims(context, threshold);
    }

    private async Task<WorkflowResult> CheckSingleClaim(
        IWorkflowContext context,
        string environment,
        TimeSpan threshold)
    {
        var claim = _store.Get(environment);
        if (claim is null)
        {
            return WorkflowResult.Ok($"Environment {environment} is not claimed");
        }

        if (claim.TimeSinceActivity < threshold)
        {
            return WorkflowResult.Ok($"Claim on {environment} is still active");
        }

        return await PromptClaimOwner(context, claim);
    }

    private async Task<WorkflowResult> SweepStaleClaims(IWorkflowContext context, TimeSpan threshold)
    {
        var staleClaims = _store.GetStale(threshold);

        if (staleClaims.Count == 0)
        {
            _log.LogDebug("No stale claims found (threshold: {Threshold})", threshold);
            return WorkflowResult.Ok("No stale claims found");
        }

        _log.LogInformation("Found {Count} stale claims to check", staleClaims.Count);

        await context.SendAsync(
            $":clock3: *Claim Expiry Check*\n\n" +
            $"Found {staleClaims.Count} environment(s) claimed for over {threshold.TotalDays} days.\n" +
            $"Checking with owners...");

        var released = 0;
        var kept = 0;
        var noResponse = 0;

        foreach (var claim in staleClaims)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var result = await PromptClaimOwner(context, claim);

            if (result.Message?.Contains("released") == true)
                released++;
            else if (result.Message?.Contains("keeping") == true)
                kept++;
            else
                noResponse++;
        }

        await context.SendAsync(
            $":clipboard: *Expiry Check Complete*\n\n" +
            $"• Released: {released}\n" +
            $"• Kept: {kept}\n" +
            $"• No response: {noResponse}");

        return WorkflowResult.Ok($"Checked {staleClaims.Count} stale claims");
    }

    private async Task<WorkflowResult> PromptClaimOwner(IWorkflowContext context, EnvironmentClaim claim)
    {
        var durationText = FormatDuration(claim.ClaimDuration);
        var activityText = FormatDuration(claim.TimeSinceActivity);

        await context.SendAsync(
            $":question: <@{claim.UserId}> - Do you still need `{claim.Environment}`?\n\n" +
            $"• *Claimed for:* {durationText}\n" +
            $"• *Last activity:* {activityText} ago\n" +
            (claim.Note is not null ? $"• *Note:* {claim.Note}\n" : ""));

        try
        {
            var stillNeeded = await context.ConfirmAsync(
                $"Keep `{claim.Environment}`?",
                timeout: TimeSpan.FromHours(24));

            if (stillNeeded)
            {
                // Owner wants to keep it - record activity to reset timer
                _store.RecordActivity(claim.Environment);

                _log.LogInformation("User {UserId} confirmed keeping {Env}",
                    claim.UserId, claim.Environment);

                await context.SendAsync(
                    $":white_check_mark: Got it! `{claim.Environment}` will remain with <@{claim.UserId}>.");

                return WorkflowResult.Ok($"{claim.Environment} - owner keeping");
            }
            else
            {
                // Owner releasing
                _store.Release(claim.Environment, claim.UserId);

                _log.LogInformation("User {UserId} released {Env} via expiry check",
                    claim.UserId, claim.Environment);

                await context.SendAsync(
                    $":unlock: `{claim.Environment}` has been released and is now available!");

                return WorkflowResult.Ok($"{claim.Environment} - released");
            }
        }
        catch (TimeoutException)
        {
            _log.LogWarning("No response from {UserId} for {Env} expiry check",
                claim.UserId, claim.Environment);

            await context.SendAsync(
                $":warning: No response from <@{claim.UserId}> for `{claim.Environment}`.\n" +
                $"The claim will remain but may be subject to `/mutiny`.");

            return WorkflowResult.Ok($"{claim.Environment} - no response");
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
