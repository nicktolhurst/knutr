namespace Knutr.Plugins.GitLabPipeline;

using Knutr.Abstractions.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGitLabPipelinePlugin(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitLabOptions>(configuration.GetSection(GitLabOptions.SectionName));

        services.AddHttpClient<IGitLabClient, GitLabClient>();

        services.AddSingleton<IBotPlugin, Plugin>();

        return services;
    }
}
