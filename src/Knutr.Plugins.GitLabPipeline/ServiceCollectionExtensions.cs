namespace Knutr.Plugins.GitLabPipeline;

using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Workflows;
using Knutr.Plugins.GitLabPipeline.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitLabPipelinePlugin(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitLabOptions>(configuration.GetSection(GitLabOptions.SectionName));

        services.AddHttpClient<IGitLabClient, GitLabClient>();

        // Environment service (can be replaced by other plugins)
        services.AddSingleton<IEnvironmentService, DefaultEnvironmentService>();

        // Workflows
        services.AddSingleton<IWorkflow, DeployWorkflow>();
        services.AddSingleton<DeployWorkflow>();

        services.AddSingleton<IBotPlugin, Plugin>();

        return services;
    }
}
