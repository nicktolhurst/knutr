namespace Knutr.Plugins.EnvironmentClaim;

/// <summary>
/// Service for managing environment claims.
/// </summary>
public interface IClaimStore
{
    /// <summary>Get a claim by environment name.</summary>
    EnvironmentClaim? Get(string environment);

    /// <summary>Get all active claims.</summary>
    IReadOnlyList<EnvironmentClaim> GetAll();

    /// <summary>Get all claims for a specific user.</summary>
    IReadOnlyList<EnvironmentClaim> GetByUser(string userId);

    /// <summary>Get claims older than specified duration.</summary>
    IReadOnlyList<EnvironmentClaim> GetStale(TimeSpan olderThan);

    /// <summary>Try to claim an environment.</summary>
    ClaimResult TryClaim(string environment, string userId, string? note = null);

    /// <summary>Release a claim. Returns true if successful.</summary>
    bool Release(string environment, string userId, bool force = false);

    /// <summary>Transfer a claim to another user (for mutiny).</summary>
    bool Transfer(string environment, string fromUserId, string toUserId);

    /// <summary>Update claim status.</summary>
    bool UpdateStatus(string environment, ClaimStatus status);

    /// <summary>Record activity on a claim.</summary>
    bool RecordActivity(string environment);

    /// <summary>Record that the claim was nudged.</summary>
    bool RecordNudge(string environment);

    /// <summary>Check if an environment is available for a user.</summary>
    bool IsAvailable(string environment, string userId);

    /// <summary>Get available environments (not claimed or claimed by the user).</summary>
    IReadOnlyList<string> GetAvailableEnvironments(string userId, IEnumerable<string> allEnvironments);
}

/// <summary>
/// Result of a claim attempt.
/// </summary>
public sealed record ClaimResult(
    bool Success,
    EnvironmentClaim? Claim = null,
    string? ErrorMessage = null,
    string? BlockedByUserId = null);
