using Knutr.Abstractions.Plugins;
using Knutr.Core.Orchestration;
using Knutr.Plugins.GitLabPipeline;

namespace Knutr.Hosting.Extensions;

public static class PluginRegistrationExtensions
{
    public static IServiceCollection AddKnutrPlugins(this IServiceCollection services, IConfiguration configuration)
    {
        // Register plugin(s)
        services.AddSingleton<IBotPlugin, Plugins.PingPong.Plugin>();
        services.AddGitLabPipelinePlugin(configuration);

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
