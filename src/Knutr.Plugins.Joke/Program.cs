using Knutr.Plugins.Joke;
using Knutr.Sdk.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKnutrPluginService<JokeHandler>();

var app = builder.Build();
app.MapKnutrPluginEndpoints();
app.Run("http://0.0.0.0:8080");
