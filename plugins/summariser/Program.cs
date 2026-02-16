using Knutr.Plugins.Summariser;
using Knutr.Sdk.Hosting;
using Knutr.Sdk.Hosting.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.AddKnutrLogging("summariser");

builder.Services.Configure<SummariserOptions>(builder.Configuration.GetSection("Summariser"));
builder.Services.Configure<SummariserOllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<SummariserSlackOptions>(builder.Configuration.GetSection("Slack"));
builder.Services.AddHttpClient("ollama", client => client.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddHttpClient("exporter-api", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("core-callback", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<OllamaHelper>();
builder.Services.AddSingleton<ExporterClient>();
builder.Services.AddSingleton<CorePostClient>();
builder.Services.AddSingleton<SummaryService>();
builder.Services.AddKnutrPluginService<SummariserHandler>();

var app = builder.Build();
app.MapKnutrPluginEndpoints();
app.Run("http://0.0.0.0:8080");
