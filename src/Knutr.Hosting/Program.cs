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
    builder.Services.AddKnutrPlugins(); // registers PingPong

    var app = builder.Build();

    // metrics, health, endpoints
    app.UseBotPrometheus();
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    // Slack webhooks
    app.MapSlackEndpoints();

    // wire orchestrator to bus
    var bus = app.Services.GetRequiredService<IEventBus>();
    var orch = app.Services.GetRequiredService<ChatOrchestrator>();
    bus.Subscribe<Knutr.Abstractions.Events.MessageContext>(orch.OnMessageAsync);
    bus.Subscribe<Knutr.Abstractions.Events.CommandContext>(orch.OnCommandAsync);

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
