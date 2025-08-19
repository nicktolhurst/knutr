using Serilog;
using Knutr.Hosting.Extensions;
using Knutr.Core.Messaging;
using Knutr.Core.Orchestration;
using Knutr.Adapters.Slack;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddKnutrObservability();

    builder.Services.AddKnutrCore(builder.Configuration);
    builder.Services.AddSlackAdapter(builder.Configuration);
    builder.Services.AddKnutrLlm(builder.Configuration);
    builder.Services.AddKnutrPlugins();

    var app = builder.Build();

    app.UseBotPrometheus();
    app.MapGet("/health", () => Results.Ok(new { status = "ok" })); // TODO: add real health checks

    // `/slack/events` and `/slack/commands`
    app.MapSlackEndpoints();

    // wire orchestrator delegates to the event bus
    var bus = app.Services.GetRequiredService<IEventBus>();
    var orch = app.Services.GetRequiredService<ChatOrchestrator>();
    bus.Subscribe<Knutr.Abstractions.Events.MessageContext>(orch.OnMessageAsync);
    bus.Subscribe<Knutr.Abstractions.Events.CommandContext>(orch.OnCommandAsync);

    Log.Logger.Information("Knutr is running on {URL}", app.Urls.FirstOrDefault() ?? "http://0.0.0.0:7071");
    app.Run("http://0.0.0.0:7071"); // TODO: make port configurable
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
