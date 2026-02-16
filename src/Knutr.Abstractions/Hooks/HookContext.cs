namespace Knutr.Abstractions.Hooks;

using System.Collections.Concurrent;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;

/// <summary>
/// Shared context that flows through the hook pipeline.
/// Provides access to command details and a shared state bag for cross-plugin communication.
/// </summary>
public sealed class HookContext
{
    private readonly ConcurrentDictionary<string, object> _bag = new();

    /// <summary>
    /// The plugin that owns this command.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// The command being executed (e.g., "knutr").
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// The action/subcommand (e.g., "deploy", "status").
    /// </summary>
    public string? Action { get; init; }

    /// <summary>
    /// Parsed arguments from the command.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// The original command context from the adapter.
    /// </summary>
    public CommandContext? CommandContext { get; init; }

    /// <summary>
    /// The original message context from the adapter (for message-triggered commands).
    /// </summary>
    public MessageContext? MessageContext { get; init; }

    /// <summary>
    /// The result from the main handler (available in AfterExecute hooks).
    /// </summary>
    public PluginResult? Result { get; set; }

    /// <summary>
    /// Any exception that occurred (available in OnError hooks).
    /// </summary>
    public Exception? Error { get; set; }

    /// <summary>
    /// Gets a value from the shared state bag.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (_bag.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    /// <summary>
    /// Sets a value in the shared state bag.
    /// </summary>
    public void Set<T>(string key, T value) where T : notnull
        => _bag[key] = value;

    /// <summary>
    /// Checks if a key exists in the shared state bag.
    /// </summary>
    public bool Has(string key) => _bag.ContainsKey(key);

    /// <summary>
    /// Removes a value from the shared state bag.
    /// </summary>
    public bool Remove(string key) => _bag.TryRemove(key, out _);

    /// <summary>
    /// Gets all keys in the shared state bag.
    /// </summary>
    public IEnumerable<string> Keys => _bag.Keys;

    /// <summary>
    /// Gets the user ID from either command or message context.
    /// </summary>
    public string? UserId => CommandContext?.UserId ?? MessageContext?.UserId;

    /// <summary>
    /// Gets the channel ID from either command or message context.
    /// </summary>
    public string? ChannelId => CommandContext?.ChannelId ?? MessageContext?.ChannelId;

    /// <summary>
    /// Gets the team ID from either command or message context.
    /// </summary>
    public string? TeamId => CommandContext?.TeamId ?? MessageContext?.TeamId;
}
