using Knutr.Plugins.JargonBuster;
using Knutr.Sdk.Hosting;
using Knutr.Sdk.Hosting.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.AddKnutrLogging("jabu");
builder.Services.AddKnutrPluginService<JargonBusterHandler>();

var app = builder.Build();
app.MapKnutrPluginEndpoints();
app.Run("http://0.0.0.0:8080");
