using Knutr.Plugins.PingPong;
using Knutr.Sdk.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKnutrPluginService<PingPongHandler>();

var app = builder.Build();
app.MapKnutrPluginEndpoints();
app.Run("http://0.0.0.0:8080");
