namespace Knutr.Sdk;

/// <summary>
/// The request sent from the core bot to a plugin service when executing a command.
/// Serialized as JSON over HTTP POST /execute.
/// </summary>
public sealed class PluginExecuteRequest
{
    /// <summary>
    /// The slash command that was invoked (e.g., "knutr", "ping").
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// The subcommand if applicable (e.g., "post-mortem", "deploy").
    /// </summary>
    public string? Subcommand { get; init; }

    /// <summary>
    /// Parsed arguments after the subcommand.
    /// </summary>
    public string[] Args { get; init; } = [];

    /// <summary>
    /// The full raw text from the user.
    /// </summary>
    public string? RawText { get; init; }

    /// <summary>
    /// Slack user ID of the invoker.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Slack channel ID where the command was invoked.
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Slack team/workspace ID.
    /// </summary>
    public string? TeamId { get; init; }

    /// <summary>
    /// Thread timestamp if the command was in a thread.
    /// </summary>
    public string? ThreadTs { get; init; }

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string? TraceId { get; init; }
}
