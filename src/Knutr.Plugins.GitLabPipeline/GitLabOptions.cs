namespace Knutr.Plugins.GitLabPipeline;

public sealed class GitLabOptions
{
    public const string SectionName = "GitLab";

    /// <summary>
    /// GitLab instance URL (e.g., https://gitlab.com or self-hosted URL).
    /// </summary>
    public string BaseUrl { get; set; } = "https://gitlab.com";

    /// <summary>
    /// Personal access token or project access token with api scope.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Default project ID or path (e.g., "mygroup/myproject" or "12345").
    /// </summary>
    public string DefaultProject { get; set; } = string.Empty;

    /// <summary>
    /// Mapping of environment names to GitLab environment scopes.
    /// </summary>
    public Dictionary<string, EnvironmentConfig> Environments { get; set; } = new();
}

public sealed class EnvironmentConfig
{
    /// <summary>
    /// GitLab environment name/scope.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Override project for this environment.
    /// </summary>
    public string? Project { get; set; }

    /// <summary>
    /// Optional: Pipeline variables to set for this environment.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();
}
