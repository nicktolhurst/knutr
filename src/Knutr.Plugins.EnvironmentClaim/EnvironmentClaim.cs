namespace Knutr.Plugins.EnvironmentClaim;

/// <summary>
/// Represents a claim on an environment.
/// </summary>
public sealed record EnvironmentClaim
{
    public required string Environment { get; init; }
    public required string UserId { get; init; }
    public required DateTime ClaimedAt { get; init; }
    public ClaimStatus Status { get; init; } = ClaimStatus.Claimed;
    public DateTime? LastDeployAt { get; init; }
    public DateTime? LastActivityAt { get; init; }
    public string? Note { get; init; }
    public int NudgeCount { get; init; } = 0;
    public DateTime? LastNudgedAt { get; init; }

    /// <summary>
    /// How long this environment has been claimed.
    /// </summary>
    public TimeSpan ClaimDuration => DateTime.UtcNow - ClaimedAt;

    /// <summary>
    /// Time since last activity (deploy or explicit activity).
    /// </summary>
    public TimeSpan TimeSinceActivity => DateTime.UtcNow - (LastActivityAt ?? LastDeployAt ?? ClaimedAt);
}

public enum ClaimStatus
{
    /// <summary>Environment is claimed and idle.</summary>
    Claimed,

    /// <summary>Deployment is in progress.</summary>
    Deploying,

    /// <summary>Owner has been nudged to release.</summary>
    Nudged,

    /// <summary>Claim is pending confirmation (for mutiny).</summary>
    PendingTransfer
}
