using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Plugins.Sentinel;

public sealed class OllamaOptions
{
    public string Url { get; set; } = "http://ollama.knutr.svc.cluster.local:11434";
    public string Model { get; set; } = "llama3.2:1b";
}

public sealed class OllamaHelper(IHttpClientFactory httpFactory, IOptions<OllamaOptions> options, ILogger<OllamaHelper> log)
{
    private readonly HttpClient _http = httpFactory.CreateClient("ollama");
    private readonly OllamaOptions _opts = options.Value;

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct)
    {
        log.LogDebug("Ollama request ({Chars} chars)", prompt.Length);
        try
        {
            var res = await _http.PostAsJsonAsync($"{_opts.Url}/api/generate",
                new { model = _opts.Model, prompt, stream = false }, ct);
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
