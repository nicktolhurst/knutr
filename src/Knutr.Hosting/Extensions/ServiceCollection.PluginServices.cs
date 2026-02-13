using Knutr.Core.PluginServices;

namespace Knutr.Hosting.Extensions;

public static class PluginServiceRegistrationExtensions
{
    /// <summary>
    /// Registers the remote plugin service infrastructure: discovery, registry, client, and dispatcher.
    /// Configured via the "PluginServices" configuration section.
    /// </summary>
    public static IServiceCollection AddKnutrPluginServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PluginServiceOptions>(configuration.GetSection(PluginServiceOptions.SectionName));

        // HTTP client for calling plugin services
        services.AddHttpClient("knutr-plugin-services", client =>
        {
            var timeout = configuration.GetValue<int?>("PluginServices:TimeoutSeconds") ?? 30;
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });

        services.AddSingleton<PluginServiceRegistry>();
        services.AddSingleton<PluginServiceClient>();
        services.AddSingleton<RemotePluginDispatcher>();

        // Background service that discovers remote plugin services at startup
        services.AddHostedService<PluginServiceDiscovery>();

        return services;
    }
}
