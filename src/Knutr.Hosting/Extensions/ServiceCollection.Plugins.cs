using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Workflows;
using Knutr.Core.Hooks;
using Knutr.Core.Orchestration;
using Knutr.Core.Workflows;
using Knutr.Plugins.EnvironmentClaim;
using Knutr.Plugins.EnvironmentClaim.Workflows;
using Knutr.Plugins.GitLabPipeline;

// Workflow button service for interactive buttons

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

        // Register workflow infrastructure
        services.AddSingleton<WorkflowEngine>();
        services.AddSingleton<IWorkflowEngine>(sp => sp.GetRequiredService<WorkflowEngine>());
        services.AddSingleton<IWorkflowButtonService, WorkflowButtonService>();

        // Register EnvironmentClaim services (must be before GitLab plugin)
        services.AddSingleton<EnvironmentClaimMetrics>();
        services.AddSingleton<IClaimStore, InMemoryClaimStore>();
        services.AddSingleton<IWorkflow, NudgeWorkflow>();
        services.AddSingleton<IWorkflow, MutinyWorkflow>();
        services.AddSingleton<IWorkflow, ClaimExpiryWorkflow>();

        // Register plugin(s)
        services.AddSingleton<IBotPlugin, Plugins.PingPong.Plugin>();
        services.AddSingleton<IBotPlugin, Plugins.EnvironmentClaim.Plugin>();

        // GitLab plugin
        services.AddGitLabPipelinePlugin(configuration);

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
