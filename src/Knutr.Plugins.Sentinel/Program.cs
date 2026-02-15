using Knutr.Plugins.Sentinel;
using Knutr.Sdk.Hosting;
using Knutr.Sdk.Hosting.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.AddKnutrLogging("sentinel");
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.AddHttpClient("ollama", client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<OllamaHelper>();
builder.Services.AddKnutrPluginService<SentinelHandler>();

var app = builder.Build();
app.MapKnutrPluginEndpoints();
app.Run("http://0.0.0.0:8080");
