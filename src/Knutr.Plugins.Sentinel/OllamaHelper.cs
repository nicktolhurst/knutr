using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Knutr.Plugins.Sentinel;

public sealed class OllamaHelper(ILogger<OllamaHelper> log)
{
    private static readonly string OllamaUrl =
        Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://ollama.knutr.svc.cluster.local:11434";

    private static readonly string Model =
        Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2:1b";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct)
    {
        log.LogDebug("Ollama request ({Chars} chars)", prompt.Length);
        try
        {
            var res = await _http.PostAsJsonAsync($"{OllamaUrl}/api/generate",
                new { model = Model, prompt, stream = false }, ct);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            var response = json.GetProperty("response").GetString() ?? "";
            log.LogDebug("Ollama response ({Chars} chars)", response.Length);
            return response;
        }
        catch (Exception ex)
        {
            log.LogWarning("Ollama call failed: {Message}", ex.Message);
            return "";
        }
    }
}
