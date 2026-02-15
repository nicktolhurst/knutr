namespace Knutr.Core.PluginServices;

using System.Collections.Concurrent;
using Knutr.Sdk;
using Microsoft.Extensions.Logging;

/// <summary>
/// Thread-safe registry of discovered remote plugin services.
/// Maintains a mapping from command/subcommand to the service that handles it.
/// </summary>
public sealed class PluginServiceRegistry(ILogger<PluginServiceRegistry> logger)
{
    private readonly ConcurrentDictionary<string, PluginServiceEntry> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PluginServiceEntry> _subcommandMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PluginServiceEntry> _slashCommandMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a discovered plugin service and index its commands.
    /// </summary>
    public void Register(PluginServiceEntry entry)
    {
        _services[entry.ServiceName] = entry;

        foreach (var sub in entry.Manifest.Subcommands)
        {
            var key = $"knutr:{sub.Name}";
            if (_subcommandMap.TryAdd(key, entry))
            {
                logger.LogInformation("Registered remote subcommand {Key} -> {Service}", key, entry.ServiceName);
            }
            else
            {
                logger.LogWarning("Remote subcommand {Key} already registered, skipping {Service}", key, entry.ServiceName);
            }
        }

        foreach (var cmd in entry.Manifest.SlashCommands)
        {
            if (_slashCommandMap.TryAdd(cmd.Command, entry))
            {
                logger.LogInformation("Registered remote slash command {Command} -> {Service}", "/" + cmd.Command, entry.ServiceName);
            }
            else
            {
                logger.LogWarning("Remote slash command {Command} already registered, skipping {Service}", "/" + cmd.Command, entry.ServiceName);
            }
        }
    }

    /// <summary>
    /// Try to find a remote plugin service that handles the given subcommand.
    /// </summary>
    public bool TryGetSubcommandService(string subcommand, out PluginServiceEntry? entry)
    {
        var key = $"knutr:{subcommand}";
        return _subcommandMap.TryGetValue(key, out entry);
    }

    /// <summary>
    /// Try to find a remote plugin service that handles the given slash command.
    /// </summary>
    public bool TryGetSlashCommandService(string command, out PluginServiceEntry? entry)
    {
        return _slashCommandMap.TryGetValue(command, out entry);
    }

    /// <summary>
    /// Get all registered service entries that declared SupportsScan in their manifest.
    /// </summary>
    public IReadOnlyList<PluginServiceEntry> GetScanCapable()
        => _services.Values.Where(e => e.Manifest.SupportsScan).ToList();

    /// <summary>
    /// Get all registered service entries.
    /// </summary>
    public IReadOnlyCollection<PluginServiceEntry> GetAll() => _services.Values.ToList().AsReadOnly();

    /// <summary>
    /// Clear all registrations (used during refresh).
    /// </summary>
    public void Clear()
    {
        _services.Clear();
        _subcommandMap.Clear();
        _slashCommandMap.Clear();
    }
}
