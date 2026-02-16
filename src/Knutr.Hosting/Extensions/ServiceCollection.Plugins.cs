using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;
using Knutr.Core.Hooks;
using Knutr.Core.Orchestration;

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

        // Register subcommand registry (allows plugins to contribute subcommands)
        services.AddSingleton<SubcommandRegistry>();
        services.AddSingleton<ISubcommandRegistry>(sp => sp.GetRequiredService<SubcommandRegistry>());
        services.AddSingleton<ISubcommandBuilder>(sp => sp.GetRequiredService<SubcommandRegistry>());

        // All plugins are now remote services — no in-process plugin registrations.

        // At startup, call Configure on each plugin with a shared CommandRegistry and HookRegistry
        services.AddHostedService<PluginConfiguratorHostedService>();
        return services;
    }
}

file sealed class PluginConfiguratorHostedService(
    IEnumerable<IBotPlugin> plugins,
    ICommandRegistry commandRegistry,
    ISubcommandRegistry subcommandRegistry,
    IHookRegistry hookRegistry,
    ILogger<PluginConfiguratorHostedService> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var context = new PluginContext((ICommandBuilder)commandRegistry, (ISubcommandBuilder)subcommandRegistry, hookRegistry);

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
