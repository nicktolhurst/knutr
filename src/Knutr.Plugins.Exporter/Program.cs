using Knutr.Plugins.Exporter;
using Knutr.Plugins.Exporter.Data;
using Knutr.Sdk.Hosting;
using Knutr.Sdk.Hosting.Logging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddKnutrLogging("exporter");

builder.Services.Configure<ExporterOptions>(builder.Configuration.GetSection("Exporter"));
builder.Services.Configure<ExporterSlackOptions>(builder.Configuration.GetSection("Slack"));
builder.Services.AddHttpClient("slack-exporter", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddDbContextFactory<ExporterDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ExporterDb")));
builder.Services.AddSingleton<SlackExportClient>();
builder.Services.AddSingleton<ExportService>();
builder.Services.AddHostedService<ExportSyncWorker>();
builder.Services.AddKnutrPluginService<ExporterHandler>();

var app = builder.Build();

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExporterDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

app.MapKnutrPluginEndpoints();
app.MapExporterApiEndpoints();
app.Run("http://0.0.0.0:8080");
