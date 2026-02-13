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
        const int maxRetries = 3;
        var delay = TimeSpan.FromSeconds(5);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            var discovered = await DiscoverAllAsync(opts, ct);

            if (discovered >= opts.Services.Count)
            {
                logger.LogInformation("All {Count} plugin services discovered", discovered);
                return;
            }

            if (attempt < maxRetries)
            {
                logger.LogWarning("Discovered {Discovered}/{Total} plugin services, retrying in {Delay}s (attempt {Attempt}/{Max})",
                    discovered, opts.Services.Count, delay.TotalSeconds, attempt, maxRetries);
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            else
            {
                logger.LogWarning("Discovered {Discovered}/{Total} plugin services after {Max} attempts. Continuing with available services",
                    discovered, opts.Services.Count, maxRetries);
            }
        }
    }

    private async Task<int> DiscoverAllAsync(PluginServiceOptions opts, CancellationToken ct)
    {
        var discovered = 0;

        var tasks = opts.Services.Select(async serviceName =>
        {
            var baseUrl = client.ResolveServiceUrl(serviceName);
            logger.LogDebug("Discovering plugin service {Name} at {Url}", serviceName, baseUrl);

            var manifest = await client.FetchManifestAsync(baseUrl, ct);
            if (manifest is null)
            {
                logger.LogWarning("Could not discover plugin service {Name} at {Url}", serviceName, baseUrl);
                return false;
            }

            registry.Register(new PluginServiceEntry
            {
                ServiceName = serviceName,
                BaseUrl = baseUrl,
                Manifest = manifest
            });

            logger.LogInformation("Discovered plugin service {Name}: {SubcommandCount} subcommands, {SlashCount} slash commands",
                manifest.Name, manifest.Subcommands.Count, manifest.SlashCommands.Count);
            return true;
        });

        var results = await Task.WhenAll(tasks);
        discovered = results.Count(r => r);
        return discovered;
    }
}
