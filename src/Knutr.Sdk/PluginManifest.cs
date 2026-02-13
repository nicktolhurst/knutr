namespace Knutr.Sdk;

/// <summary>
/// Declares what a plugin service handles. Returned by GET /manifest
/// so the core bot knows how to route commands to this service.
/// </summary>
public sealed class PluginManifest
{
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Subcommands this plugin handles under /knutr (e.g., "post-mortem", "export").
    /// </summary>
    public IReadOnlyList<PluginSubcommand> Subcommands { get; init; } = [];

    /// <summary>
    /// Standalone slash commands this plugin handles (e.g., "/ping").
    /// </summary>
    public IReadOnlyList<PluginSlashCommand> SlashCommands { get; init; } = [];

    /// <summary>
    /// If true, the core bot will broadcast every channel message to this service
    /// via POST /scan, allowing it to passively react to message content.
    /// </summary>
    public bool SupportsScan { get; init; }
}

public sealed class PluginSubcommand
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public sealed class PluginSlashCommand
{
    public required string Command { get; init; }
    public string? Description { get; init; }
}
