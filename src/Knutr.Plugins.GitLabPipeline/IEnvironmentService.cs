namespace Knutr.Plugins.GitLabPipeline;

using Microsoft.Extensions.Options;

/// <summary>
/// Service for checking environment availability and status.
/// This abstraction allows different implementations (e.g., claim-based).
/// </summary>
public interface IEnvironmentService
{
    /// <summary>
    /// Check if an environment is available for deployment.
    /// </summary>
    Task<EnvironmentAvailability> CheckAvailabilityAsync(string environment, string userId);

    /// <summary>
    /// Get list of available environments for a user.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableEnvironmentsAsync(string userId);
}

/// <summary>
/// Result of an environment availability check.
/// </summary>
public sealed record EnvironmentAvailability(
    bool IsAvailable,
    string? ClaimedBy = null,
    DateTime? ClaimedAt = null,
    string? Status = null);

/// <summary>
/// Default implementation that always returns available.
/// </summary>
public sealed class DefaultEnvironmentService : IEnvironmentService
{
    private readonly GitLabOptions _options;

    public DefaultEnvironmentService(IOptions<GitLabOptions> options)
    {
        _options = options.Value;
    }

    public Task<EnvironmentAvailability> CheckAvailabilityAsync(string environment, string userId)
    {
        return Task.FromResult(new EnvironmentAvailability(IsAvailable: true));
    }

    public Task<IReadOnlyList<string>> GetAvailableEnvironmentsAsync(string userId)
    {
        var envs = _options.Environments.Keys.ToList();
        return Task.FromResult<IReadOnlyList<string>>(envs);
    }
}
