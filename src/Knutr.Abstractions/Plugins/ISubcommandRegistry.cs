namespace Knutr.Abstractions.Plugins;

using Knutr.Abstractions.Events;

/// <summary>
/// Registry for plugin subcommands under a parent command (e.g., /knutr).
/// Allows plugins to register subcommands without direct dependencies on each other.
/// </summary>
public interface ISubcommandRegistry
{
    /// <summary>
    /// Register a subcommand handler.
    /// </summary>
    /// <param name="parentCommand">The parent command (e.g., "knutr").</param>
    /// <param name="subcommand">The subcommand name (e.g., "claim", "deploy").</param>
    /// <param name="handler">Handler function that receives context and parsed arguments.</param>
    void Register(string parentCommand, string subcommand, SubcommandHandler handler);

    /// <summary>
    /// Try to find a handler for a subcommand.
    /// </summary>
    /// <param name="parentCommand">The parent command.</param>
    /// <param name="subcommand">The subcommand name.</param>
    /// <param name="handler">The handler if found.</param>
    /// <returns>True if a handler was found.</returns>
    bool TryGetHandler(string parentCommand, string subcommand, out SubcommandHandler? handler);

    /// <summary>
    /// Get all registered subcommands for a parent command.
    /// </summary>
    IReadOnlyList<string> GetSubcommands(string parentCommand);
}

/// <summary>
/// Handler delegate for subcommands.
/// </summary>
/// <param name="ctx">The command context.</param>
/// <param name="args">Parsed arguments (everything after the subcommand).</param>
/// <returns>The plugin result.</returns>
public delegate Task<PluginResult> SubcommandHandler(CommandContext ctx, string[] args);

/// <summary>
/// Builder interface for registering subcommands (plugin-facing).
/// </summary>
public interface ISubcommandBuilder
{
    /// <summary>
    /// Register a subcommand under /knutr.
    /// </summary>
    ISubcommandBuilder Subcommand(string name, SubcommandHandler handler);
}
