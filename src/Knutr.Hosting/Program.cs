using Serilog;
using Knutr.Hosting.Extensions;
using Knutr.Core.Messaging;
using Knutr.Core.Orchestration;
using Knutr.Abstractions.Events;
using Knutr.Adapters.Slack;
using Knutr.Abstractions.Messaging;
using Knutr.Core.Channels;
using Knutr.Sdk.Hosting.Logging;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddKnutrLogging("core");

    builder.Services.AddKnutrCore(builder.Configuration);
    builder.Services.AddSlackAdapter(builder.Configuration);
    builder.Services.AddKnutrLlm(builder.Configuration);
    builder.Services.AddKnutrPlugins(builder.Configuration);
    builder.Services.AddKnutrPluginServices(builder.Configuration);

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    // `/slack/events` and `/slack/commands`
    app.MapSlackEndpoints();

    // Internal callback for plugins to post messages via the core
    app.MapPost("/internal/post", async (InternalPostRequest req, IMessagingService messaging, CancellationToken ct) =>
    {
        var ts = await messaging.PostMessageAsync(req.ChannelId, req.Text, req.ThreadTs, ct);
        return Results.Ok(new { ok = ts is not null, messageTs = ts });
    });

    // wire orchestrator delegates to the event bus
    var bus = app.Services.GetRequiredService<IEventBus>();
    var orch = app.Services.GetRequiredService<ChatOrchestrator>();
    bus.Subscribe<Knutr.Abstractions.Events.MessageContext>(orch.OnMessageAsync);
    bus.Subscribe<Knutr.Abstractions.Events.CommandContext>(orch.OnCommandAsync);

    var reactionHandler = app.Services.GetRequiredService<ReactionHandler>();
    bus.Subscribe<Knutr.Abstractions.Events.ReactionContext>(reactionHandler.OnReactionAsync);

    var channelPolicy = app.Services.GetRequiredService<ChannelPolicy>();
    var membershipLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ChannelMembership");
    bus.Subscribe<ChannelMembershipContext>((ctx, _) =>
    {
        if (ctx.Joined)
        {
            var plugins = channelPolicy.GetEnabledPlugins(ctx.ChannelId);
            var enabled = channelPolicy.IsChannelAllowed(ctx.ChannelId);
            var pluginList = plugins.Count > 0 ? string.Join(", ", plugins) : "all";
            membershipLogger.LogInformation("Channel: Bot has joined \"{ChannelId}\" (enabled={Enabled}, plugins=[{Plugins}])",
                ctx.ChannelId, enabled, pluginList);
        }
        else
        {
            membershipLogger.LogInformation("Channel: Bot has left \"{ChannelId}\"", ctx.ChannelId);
        }
        return Task.CompletedTask;
    });

    Log.Information("Knutr is running on {URL}", "http://0.0.0.0:7071");
    app.Run("http://0.0.0.0:7071");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

record InternalPostRequest(string ChannelId, string Text, string? ThreadTs = null);
