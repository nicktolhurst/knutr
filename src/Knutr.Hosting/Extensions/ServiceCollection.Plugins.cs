using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;
using Knutr.Core.Hooks;
using Knutr.Core.Orchestration;
using Knutr.Plugins.GitLabPipeline;

namespace Knutr.Hosting.Extensions;

public static class PluginRegistrationExtensions
{
    public static IServiceCollection AddKnutrPlugins(this IServiceCollection services, IConfiguration configuration)
    {
        // Register hook infrastructure
        services.AddSingleton<HookRegistry>();
        services.AddSingleton<IHookRegistry>(sp => sp.GetRequiredService<HookRegistry>());
        services.AddSingleton<IHookBuilder>(sp => sp.GetRequiredService<HookRegistry>());
        services.AddSingleton<HookPipeline>();

        // Register plugin(s)
        services.AddSingleton<IBotPlugin, Plugins.PingPong.Plugin>();
        services.AddGitLabPipelinePlugin(configuration);
        services.AddSingleton<IBotPlugin, Plugins.EnvironmentClaim.Plugin>();

        // At startup, call Configure on each plugin with a shared CommandRegistry and HookRegistry
        services.AddHostedService<PluginConfiguratorHostedService>();
        return services;
    }
}

file sealed class PluginConfiguratorHostedService(
    IEnumerable<IBotPlugin> plugins,
    ICommandRegistry commandRegistry,
    IHookRegistry hookRegistry,
    ILogger<PluginConfiguratorHostedService> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var context = new PluginContext((ICommandBuilder)commandRegistry, hookRegistry);

        foreach (var plugin in plugins)
        {
            plugin.Configure(context);
            log.LogInformation("Registered: {Plugin} (hooks: {HookCount})",
                plugin.Name,
                hookRegistry.CountHooks(HookPoint.Validate) +
                hookRegistry.CountHooks(HookPoint.BeforeExecute) +
                hookRegistry.CountHooks(HookPoint.AfterExecute) +
                hookRegistry.CountHooks(HookPoint.OnError));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
