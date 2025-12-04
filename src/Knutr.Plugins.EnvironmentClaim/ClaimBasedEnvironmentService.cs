namespace Knutr.Plugins.EnvironmentClaim;

using Knutr.Plugins.GitLabPipeline;
using Microsoft.Extensions.Options;

/// <summary>
/// Implementation of IEnvironmentService that uses the claim store
/// to determine environment availability.
/// </summary>
public sealed class ClaimBasedEnvironmentService : IEnvironmentService
{
    private readonly IClaimStore _claimStore;
    private readonly GitLabOptions _gitLabOptions;

    public ClaimBasedEnvironmentService(IClaimStore claimStore, IOptions<GitLabOptions> gitLabOptions)
    {
        _claimStore = claimStore;
        _gitLabOptions = gitLabOptions.Value;
    }

    public Task<EnvironmentAvailability> CheckAvailabilityAsync(string environment, string userId)
    {
        var claim = _claimStore.Get(environment);

        if (claim is null)
        {
            return Task.FromResult(new EnvironmentAvailability(IsAvailable: true));
        }

        if (claim.UserId == userId)
        {
            // User owns the claim - it's available to them
            return Task.FromResult(new EnvironmentAvailability(
                IsAvailable: true,
                ClaimedBy: claim.UserId,
                ClaimedAt: claim.ClaimedAt,
                Status: claim.Status.ToString()));
        }

        // Claimed by someone else
        return Task.FromResult(new EnvironmentAvailability(
            IsAvailable: false,
            ClaimedBy: claim.UserId,
            ClaimedAt: claim.ClaimedAt,
            Status: claim.Status.ToString()));
    }

    public Task<IReadOnlyList<string>> GetAvailableEnvironmentsAsync(string userId)
    {
        var allEnvironments = _gitLabOptions.Environments.Keys;
        var available = _claimStore.GetAvailableEnvironments(userId, allEnvironments);
        return Task.FromResult(available);
    }
}
