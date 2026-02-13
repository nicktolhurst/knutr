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

    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}",
            theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code));

    builder.Services.AddKnutrCore(builder.Configuration);
    builder.Services.AddSlackAdapter(builder.Configuration);
    builder.Services.AddKnutrLlm(builder.Configuration);
    builder.Services.AddKnutrPlugins(builder.Configuration);
    builder.Services.AddKnutrPluginServices(builder.Configuration);

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    // `/slack/events` and `/slack/commands`
    app.MapSlackEndpoints();

    // wire orchestrator delegates to the event bus
    var bus = app.Services.GetRequiredService<IEventBus>();
    var orch = app.Services.GetRequiredService<ChatOrchestrator>();
    bus.Subscribe<Knutr.Abstractions.Events.MessageContext>(orch.OnMessageAsync);
    bus.Subscribe<Knutr.Abstractions.Events.CommandContext>(orch.OnCommandAsync);

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
