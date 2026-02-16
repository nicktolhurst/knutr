namespace Knutr.Core.PluginServices;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that discovers remote plugin services at startup.
/// Fetches manifests from configured service endpoints and registers them
/// in the PluginServiceRegistry for command routing.
/// </summary>
public sealed class PluginServiceDiscovery(
    PluginServiceRegistry registry,
    PluginServiceClient client,
    IOptions<PluginServiceOptions> options,
    ILogger<PluginServiceDiscovery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        if (opts.Services.Count == 0)
        {
            logger.LogInformation("No remote plugin services configured");
            return;
        }

        // Initial discovery with retry for services that may not be ready yet
        await DiscoverWithRetryAsync(opts, stoppingToken);

        // Periodic refresh if configured
        if (opts.RefreshIntervalSeconds > 0)
        {
            var interval = TimeSpan.FromSeconds(opts.RefreshIntervalSeconds);
            logger.LogInformation("Plugin service refresh enabled every {Interval}s", opts.RefreshIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(interval, stoppingToken);
                await DiscoverAllAsync(opts, stoppingToken);
            }
        }
    }

    private async Task DiscoverWithRetryAsync(PluginServiceOptions opts, CancellationToken ct)
    {
        const int maxRetries = 5;
        var delay = TimeSpan.FromSeconds(5);
        var pending = new HashSet<string>(opts.Services, StringComparer.OrdinalIgnoreCase);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            var found = await DiscoverServicesAsync(pending, ct);
            pending.ExceptWith(found);

            if (pending.Count == 0)
            {
                logger.LogInformation("All {Count} plugin services discovered", opts.Services.Count);
                return;
            }

            if (attempt < maxRetries)
            {
                logger.LogWarning("Discovered {Discovered}/{Total} plugin services, retrying {Pending} pending in {Delay}s (attempt {Attempt}/{Max})",
                    opts.Services.Count - pending.Count, opts.Services.Count, pending.Count, delay.TotalSeconds, attempt, maxRetries);
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            else
            {
                logger.LogWarning("Discovered {Discovered}/{Total} plugin services after {Max} attempts. Continuing with available services",
                    opts.Services.Count - pending.Count, opts.Services.Count, maxRetries);
            }
        }
    }

    private async Task<int> DiscoverAllAsync(PluginServiceOptions opts, CancellationToken ct)
    {
        var found = await DiscoverServicesAsync(opts.Services, ct);
        return found.Count;
    }

    private async Task<List<string>> DiscoverServicesAsync(IEnumerable<string> serviceNames, CancellationToken ct)
    {
        var found = new List<string>();

        // Phase 1: Fetch all manifests in parallel
        var tasks = serviceNames.Select(async serviceName =>
        {
            var baseUrl = client.ResolveServiceUrl(serviceName);
            logger.LogDebug("Discovering plugin service {Name} at {Url}", serviceName, baseUrl);

            var manifest = await client.FetchManifestAsync(baseUrl, ct);
            if (manifest is null)
            {
                logger.LogWarning("Could not discover plugin service {Name} at {Url}", serviceName, baseUrl);
                return (serviceName, manifest: (Sdk.PluginManifest?)null, baseUrl);
            }

            return (serviceName, manifest: (Sdk.PluginManifest?)manifest, baseUrl);
        });

        var results = await Task.WhenAll(tasks);

        var candidates = new Dictionary<string, (Sdk.PluginManifest Manifest, string BaseUrl)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (serviceName, manifest, baseUrl) in results)
        {
            if (manifest is not null)
                candidates[serviceName] = (manifest, baseUrl);
        }

        // Phase 2: Validate dependencies
        var approved = ValidateDependencies(candidates);

        // Phase 3: Register only approved services
        foreach (var serviceName in approved)
        {
            var (manifest, baseUrl) = candidates[serviceName];

            registry.Register(new PluginServiceEntry
            {
                ServiceName = serviceName,
                BaseUrl = baseUrl,
                Manifest = manifest
            });

            logger.LogInformation("Discovered plugin service {Name}: {SubcommandCount} subcommands, {SlashCount} slash commands, scan={SupportsScan}",
                manifest.Name, manifest.Subcommands.Count, manifest.SlashCommands.Count, manifest.SupportsScan);
            found.Add(serviceName);
        }

        return found;
    }

    /// <summary>
    /// Validates plugin dependencies, detecting missing dependencies and cycles.
    /// Returns the list of service names that passed validation.
    /// </summary>
    private List<string> ValidateDependencies(Dictionary<string, (Sdk.PluginManifest Manifest, string BaseUrl)> candidates)
    {
        // Build adjacency list: service -> services it depends on
        var dependencies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, (manifest, _)) in candidates)
        {
            dependencies[name] = manifest.Dependencies
                .Select(d => d) // copy
                .ToList();
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Detect cycles using DFS with coloring
        // White = unvisited, Gray = in current path, Black = fully processed
        var white = 0; var gray = 1; var black = 2;
        var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in candidates.Keys)
        {
            color[name] = white;
            parent[name] = null;
        }

        foreach (var name in candidates.Keys)
        {
            if (color[name] == white && DetectCycle(name, color, parent, dependencies, candidates, excluded, white, gray, black))
            {
                // Cycle members already added to excluded inside DetectCycle
            }
        }

        // Check for missing dependencies (including transitive exclusions)
        bool changed;
        do
        {
            changed = false;
            foreach (var (name, deps) in dependencies)
            {
                if (excluded.Contains(name)) continue;

                foreach (var dep in deps)
                {
                    if ((!candidates.ContainsKey(dep) && !registry.IsServiceRegistered(dep)) || excluded.Contains(dep))
                    {
                        logger.LogWarning("Plugin \"{Name}\" skipped: dependency \"{Dependency}\" is not available",
                            name, dep);
                        excluded.Add(name);
                        changed = true;
                        break;
                    }
                }
            }
        } while (changed); // Re-check in case excluding a service breaks another's dependency

        return candidates.Keys
            .Where(name => !excluded.Contains(name))
            .ToList();
    }

    /// <summary>
    /// DFS cycle detection. Returns true if a cycle was found starting from node.
    /// </summary>
    private bool DetectCycle(
        string node,
        Dictionary<string, int> color,
        Dictionary<string, string?> parent,
        Dictionary<string, List<string>> dependencies,
        Dictionary<string, (Sdk.PluginManifest, string)> candidates,
        HashSet<string> excluded,
        int white, int gray, int black)
    {
        color[node] = gray;
        var foundCycle = false;

        if (dependencies.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps)
            {
                // Only follow edges to known candidates
                if (!candidates.ContainsKey(dep)) continue;

                if (color.TryGetValue(dep, out var depColor) && depColor == gray)
                {
                    // Found a cycle — trace it back
                    var cycle = new List<string> { dep, node };
                    var current = node;
                    while (parent.TryGetValue(current, out var p) && p is not null && !string.Equals(p, dep, StringComparison.OrdinalIgnoreCase))
                    {
                        cycle.Add(p);
                        current = p;
                    }
                    cycle.Reverse();

                    var cycleStr = string.Join(" -> ", cycle) + " -> " + cycle[0];
                    logger.LogError("Plugin dependency cycle detected: {Cycle}. Skipping cycle members", cycleStr);

                    foreach (var member in cycle)
                        excluded.Add(member);

                    foundCycle = true;
                }
                else if (color.TryGetValue(dep, out var dc) && dc == white)
                {
                    parent[dep] = node;
                    if (DetectCycle(dep, color, parent, dependencies, candidates, excluded, white, gray, black))
                        foundCycle = true;
                }
            }
        }

        color[node] = black;
        return foundCycle;
    }
}
