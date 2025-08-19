using Knutr.Abstractions.Plugins;
using Knutr.Core.Orchestration;

namespace Knutr.Hosting.Extensions;

public static class PluginRegistrationExtensions
{
    public static IServiceCollection AddKnutrPlugins(this IServiceCollection services)
    {
        // Register plugin(s)
        services.AddSingleton<IBotPlugin, Plugins.PingPong.Plugin>();

        // At startup, call Configure on each plugin with a shared CommandRegistry
        services.AddHostedService<PluginConfiguratorHostedService>();
        return services;
    }
}

file sealed class PluginConfiguratorHostedService(
    IEnumerable<IBotPlugin> plugins,
    ICommandRegistry registry,
    ILogger<PluginConfiguratorHostedService> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in plugins)
        {
            plugin.Configure((ICommandBuilder)registry);
            log.LogInformation("Registered: {Plugin}", plugin.Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
