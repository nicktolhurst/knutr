namespace Knutr.Core.Orchestration;

using System.Collections.Concurrent;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;

/// <summary>
/// Registry for plugin subcommands.
/// Thread-safe implementation using concurrent dictionary.
/// </summary>
public sealed class SubcommandRegistry : ISubcommandRegistry, ISubcommandBuilder
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SubcommandHandler>> _handlers = new();

    public void Register(string parentCommand, string subcommand, SubcommandHandler handler)
    {
        var parent = Normalize(parentCommand);
        var sub = Normalize(subcommand);

        var subcommands = _handlers.GetOrAdd(parent, _ => new ConcurrentDictionary<string, SubcommandHandler>());
        subcommands[sub] = handler;
    }

    public bool TryGetHandler(string parentCommand, string subcommand, out SubcommandHandler? handler)
    {
        handler = null;
        var parent = Normalize(parentCommand);
        var sub = Normalize(subcommand);

        if (!_handlers.TryGetValue(parent, out var subcommands))
            return false;

        return subcommands.TryGetValue(sub, out handler);
    }

    public IReadOnlyList<string> GetSubcommands(string parentCommand)
    {
        var parent = Normalize(parentCommand);
        if (!_handlers.TryGetValue(parent, out var subcommands))
            return Array.Empty<string>();

        return subcommands.Keys.ToList();
    }

    // ISubcommandBuilder implementation (registers under "knutr" by default)
    public ISubcommandBuilder Subcommand(string name, SubcommandHandler handler)
    {
        Register("knutr", name, handler);
        return this;
    }

    private static string Normalize(string s) => s.Trim().ToLowerInvariant();
}
