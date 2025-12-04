namespace Knutr.Plugins.EnvironmentClaim;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

/// <summary>
/// In-memory implementation of IClaimStore.
/// Replace with persistent storage for production use.
/// </summary>
public sealed class InMemoryClaimStore : IClaimStore
{
    private readonly ConcurrentDictionary<string, EnvironmentClaim> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<InMemoryClaimStore> _log;

    public InMemoryClaimStore(ILogger<InMemoryClaimStore> log)
    {
        _log = log;
    }

    public EnvironmentClaim? Get(string environment)
    {
        _claims.TryGetValue(environment, out var claim);
        return claim;
    }

    public IReadOnlyList<EnvironmentClaim> GetAll()
        => _claims.Values.ToList();

    public IReadOnlyList<EnvironmentClaim> GetByUser(string userId)
        => _claims.Values.Where(c => c.UserId == userId).ToList();

    public IReadOnlyList<EnvironmentClaim> GetStale(TimeSpan olderThan)
        => _claims.Values.Where(c => c.TimeSinceActivity > olderThan).ToList();

    public ClaimResult TryClaim(string environment, string userId, string? note = null)
    {
        if (_claims.TryGetValue(environment, out var existing))
        {
            if (existing.UserId == userId)
            {
                return new ClaimResult(true, existing, "You already have this environment claimed");
            }

            return new ClaimResult(
                false,
                ErrorMessage: $"Environment is claimed by another user since {existing.ClaimedAt:g}",
                BlockedByUserId: existing.UserId);
        }

        var claim = new EnvironmentClaim
        {
            Environment = environment,
            UserId = userId,
            ClaimedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            Note = note
        };

        if (_claims.TryAdd(environment, claim))
        {
            _log.LogInformation("User {UserId} claimed environment {Environment}", userId, environment);
            return new ClaimResult(true, claim);
        }

        // Race condition - someone else claimed it
        _claims.TryGetValue(environment, out existing);
        return new ClaimResult(
            false,
            ErrorMessage: "Environment was just claimed by another user",
            BlockedByUserId: existing?.UserId);
    }

    public bool Release(string environment, string userId, bool force = false)
    {
        if (!_claims.TryGetValue(environment, out var existing))
        {
            return false;
        }

        if (!force && existing.UserId != userId)
        {
            return false;
        }

        var removed = _claims.TryRemove(environment, out _);
        if (removed)
        {
            _log.LogInformation("User {UserId} released environment {Environment} (force: {Force})",
                userId, environment, force);
        }

        return removed;
    }

    public bool Transfer(string environment, string fromUserId, string toUserId)
    {
        if (!_claims.TryGetValue(environment, out var existing))
        {
            return false;
        }

        if (existing.UserId != fromUserId)
        {
            return false;
        }

        var newClaim = new EnvironmentClaim
        {
            Environment = environment,
            UserId = toUserId,
            ClaimedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            Note = $"Transferred from <@{fromUserId}>"
        };

        _claims[environment] = newClaim;

        _log.LogInformation("Environment {Environment} transferred from {FromUser} to {ToUser}",
            environment, fromUserId, toUserId);

        return true;
    }

    public bool UpdateStatus(string environment, ClaimStatus status)
    {
        if (!_claims.TryGetValue(environment, out var existing))
        {
            return false;
        }

        _claims[environment] = existing with { Status = status };
        return true;
    }

    public bool RecordActivity(string environment)
    {
        if (!_claims.TryGetValue(environment, out var existing))
        {
            return false;
        }

        _claims[environment] = existing with { LastActivityAt = DateTime.UtcNow };
        return true;
    }

    public bool RecordNudge(string environment)
    {
        if (!_claims.TryGetValue(environment, out var existing))
        {
            return false;
        }

        _claims[environment] = existing with
        {
            NudgeCount = existing.NudgeCount + 1,
            LastNudgedAt = DateTime.UtcNow,
            Status = ClaimStatus.Nudged
        };

        return true;
    }

    public bool IsAvailable(string environment, string userId)
    {
        if (!_claims.TryGetValue(environment, out var existing))
        {
            return true; // Not claimed
        }

        return existing.UserId == userId; // Available if claimed by same user
    }

    public IReadOnlyList<string> GetAvailableEnvironments(string userId, IEnumerable<string> allEnvironments)
    {
        return allEnvironments
            .Where(env => IsAvailable(env, userId))
            .ToList();
    }
}
