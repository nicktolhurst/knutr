using System.Net.Http.Json;
using System.Text.Json;
using Knutr.Abstractions.NL;
using Microsoft.Extensions.Options;

namespace Knutr.Infrastructure.Llm;

public sealed class OllamaClient(HttpClient http, IOptions<LlmClientOptions> opt) : ILlmClient
{
    private readonly LlmClientOptions opt = opt.Value;

    public async Task<string> CompleteAsync(string system, string prompt, CancellationToken ct = default)
    {
        var res = await http.PostAsJsonAsync("/api/generate", new { model = opt.Model, prompt = $"{system}\n\n{prompt}" }, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";
    }
}
