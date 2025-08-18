using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Knutr.Abstractions.NL;
using Microsoft.Extensions.Options;

namespace Knutr.Infrastructure.Llm;

public sealed class OpenAIChatClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly LlmClientOptions _opt;

    public OpenAIChatClient(HttpClient http, IOptions<LlmClientOptions> opt)
    {
        _http = http; _opt = opt.Value;
    }

    public async Task<string> CompleteAsync(string system, string prompt, CancellationToken ct = default)
    {
        var req = new
        {
            model = _opt.Model,
            messages = new object[] {
                new { role = "system", content = system },
                new { role = "user", content = prompt }
            }
        };
        var res = await _http.PostAsJsonAsync("chat/completions", req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return content;
    }
}
