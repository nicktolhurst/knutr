using Microsoft.Extensions.DependencyInjection;
using Knutr.Abstractions.Plugins;
using Knutr.Core.Orchestration;

namespace Knutr.Hosting.Extensions;

public static class PluginRegistrationExtensions
{
    public static IServiceCollection AddKnutrPlugins(this IServiceCollection services)
    {
        // Register plugin(s)
        services.AddSingleton<IBotPlugin, Knutr.Plugins.PingPong.Plugin>();

        // At startup, call Configure on each plugin with a shared CommandRegistry
        services.AddHostedService<PluginConfiguratorHostedService>();
        return services;
    }
}

file sealed class PluginConfiguratorHostedService : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly IEnumerable<IBotPlugin> _plugins;
    private readonly ICommandRegistry _registry;

    public PluginConfiguratorHostedService(IEnumerable<IBotPlugin> plugins, ICommandRegistry registry)
    {
        _plugins = plugins; _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var p in _plugins) p.Configure((Knutr.Abstractions.Plugins.ICommandBuilder)_registry);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
