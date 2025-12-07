using System.Diagnostics.Metrics;

namespace Knutr.Plugins.EnvironmentClaim;

/// <summary>
/// Metrics for EnvironmentClaim plugin operations.
/// </summary>
public sealed class EnvironmentClaimMetrics
{
    private readonly Meter _meter;

    /// <summary>Total claim operations. Tags: operation (claim/release/transfer/nudge/mutiny), outcome (success/failure)</summary>
    public Counter<long> ClaimOperations { get; }

    /// <summary>Current active claims. Tags: environment, status</summary>
    public UpDownCounter<long> ActiveClaims { get; }

    /// <summary>Claim duration in hours when released. Tags: environment</summary>
    public Histogram<double> ClaimDuration { get; }

    /// <summary>Claim depth (number of concurrent claims per user). Tags: user</summary>
    public Histogram<long> ClaimDepth { get; }

    /// <summary>Nudge count per claim before release. Tags: environment</summary>
    public Histogram<long> NudgesBeforeRelease { get; }

    /// <summary>Deploy operations. Tags: environment, outcome</summary>
    public Counter<long> DeployOperations { get; }

    /// <summary>Time since last activity when nudged. Tags: environment</summary>
    public Histogram<double> InactivityBeforeNudge { get; }

    public EnvironmentClaimMetrics()
    {
        _meter = new("Knutr.Plugins.EnvironmentClaim");

        ClaimOperations = _meter.CreateCounter<long>(
            "knutr_claim_operations_total",
            description: "Total claim operations by type and outcome");

        ActiveClaims = _meter.CreateUpDownCounter<long>(
            "knutr_active_claims",
            description: "Current number of active claims by environment");

        ClaimDuration = _meter.CreateHistogram<double>(
            "knutr_claim_duration_hours",
            unit: "hours",
            description: "Duration of claims when released");

        ClaimDepth = _meter.CreateHistogram<long>(
            "knutr_claim_depth",
            description: "Number of concurrent claims per user");

        NudgesBeforeRelease = _meter.CreateHistogram<long>(
            "knutr_nudges_before_release",
            description: "Number of nudges received before releasing a claim");

        DeployOperations = _meter.CreateCounter<long>(
            "knutr_deploy_operations_total",
            description: "Total deploy operations by environment and outcome");

        InactivityBeforeNudge = _meter.CreateHistogram<double>(
            "knutr_inactivity_before_nudge_hours",
            unit: "hours",
            description: "Time since last activity when a claim is nudged");
    }

    public void RecordClaimOperation(string operation, string outcome, string? environment = null)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new("operation", operation),
            new("outcome", outcome)
        };
        if (environment != null)
        {
            tags.Add(new("environment", environment));
        }
        ClaimOperations.Add(1, tags.ToArray());
    }

    public void ClaimCreated(string environment)
    {
        ActiveClaims.Add(1, [new("environment", environment)]);
    }

    public void ClaimReleased(string environment, TimeSpan duration, int nudgeCount)
    {
        ActiveClaims.Add(-1, [new("environment", environment)]);
        ClaimDuration.Record(duration.TotalHours, [new("environment", environment)]);
        NudgesBeforeRelease.Record(nudgeCount, [new("environment", environment)]);
    }

    public void RecordClaimDepth(string userId, int depth)
    {
        ClaimDepth.Record(depth, [new("user", userId)]);
    }

    public void RecordDeploy(string environment, string outcome)
    {
        DeployOperations.Add(1, [
            new("environment", environment),
            new("outcome", outcome)
        ]);
    }

    public void RecordNudge(string environment, TimeSpan timeSinceActivity)
    {
        InactivityBeforeNudge.Record(timeSinceActivity.TotalHours, [new("environment", environment)]);
    }
}
