namespace Knutr.Plugins.GitLabPipeline;

/// <summary>
/// Service for checking environment availability and status.
/// This abstraction allows different implementations (e.g., EnvironmentClaim plugin).
/// </summary>
public interface IEnvironmentService
{
    /// <summary>
    /// Check if an environment is available for deployment.
    /// </summary>
    /// <param name="environment">Environment name.</param>
    /// <param name="userId">User requesting access.</param>
    /// <returns>Availability status.</returns>
    Task<EnvironmentAvailability> CheckAvailabilityAsync(string environment, string userId);

    /// <summary>
    /// Get list of available environments for a user.
    /// </summary>
    /// <param name="userId">User requesting the list.</param>
    /// <returns>List of available environment names.</returns>
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
/// Replace with integration to EnvironmentClaim plugin or other service.
/// </summary>
public sealed class DefaultEnvironmentService : IEnvironmentService
{
    private readonly GitLabOptions _options;

    public DefaultEnvironmentService(Microsoft.Extensions.Options.IOptions<GitLabOptions> options)
    {
        _options = options.Value;
    }

    public Task<EnvironmentAvailability> CheckAvailabilityAsync(string environment, string userId)
    {
        // Default: always available
        return Task.FromResult(new EnvironmentAvailability(IsAvailable: true));
    }

    public Task<IReadOnlyList<string>> GetAvailableEnvironmentsAsync(string userId)
    {
        // Return all configured environments
        var envs = _options.Environments.Keys.ToList();
        return Task.FromResult<IReadOnlyList<string>>(envs);
    }
}
