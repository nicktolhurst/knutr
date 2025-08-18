using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Knutr.Abstractions.NL;
using Knutr.Infrastructure.Llm;
using System.Net.Http.Headers;

namespace Knutr.Hosting.Extensions;

public static class LlmExtensions
{
    public static IServiceCollection AddKnutrLlm(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<LlmClientOptions>(cfg.GetSection("LLM"));
        var provider = cfg.GetValue<string>("LLM:Provider") ?? "Ollama";

        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ILlmClient, OpenAIChatClient>((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<LlmClientOptions>>().Value;
                var baseUrl = opt.BaseUrl ?? "https://api.openai.com/v1";
                client.BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/");
                if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opt.ApiKey);
            });
        }
        else // Ollama default
        {
            services.AddHttpClient<ILlmClient, OllamaClient>((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<LlmClientOptions>>().Value;
                var baseUrl = opt.BaseUrl ?? "http://localhost:11434";
                client.BaseAddress = new Uri(baseUrl);
            });
        }

        return services;
    }
}
