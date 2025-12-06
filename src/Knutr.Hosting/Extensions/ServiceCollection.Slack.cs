using Knutr.Adapters.Slack;
using Knutr.Core.Workflows;

namespace Knutr.Hosting.Extensions;

public static class SlackExtensions
{
    public static IServiceCollection AddSlackAdapter(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<SlackOptions>(cfg.GetSection("Slack"));
        services.AddHttpClient("slack");
        services.AddHostedService<SlackEgressWorker>(); // worker for outgoing messages
        services.AddSingleton<IThreadedMessagingService, SlackThreadedMessagingService>();
        return services;
    }
}
