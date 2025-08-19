using Knutr.Adapters.Slack;

namespace Knutr.Hosting.Extensions;

public static class SlackExtensions
{
    public static IServiceCollection AddSlackAdapter(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<SlackOptions>(cfg.GetSection("Slack"));
        services.AddHttpClient("slack");
        services.AddHostedService<SlackEgressWorker>(); // worker for outgoing messages
        return services;
    }
}
