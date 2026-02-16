using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Knutr.Abstractions.NL;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Infrastructure.Llm;

public sealed class OpenAIChatClient(HttpClient http, IOptions<LlmClientOptions> opt, ILogger<OpenAIChatClient> logger) : ILlmClient
{
    private readonly LlmClientOptions _opt = opt.Value;

    public async Task<string> CompleteAsync(string system, string prompt, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var req = new
            {
                model = _opt.Model,
                messages = new object[] {
                    new { role = "system", content = system },
                    new { role = "user", content = prompt }
                }
            };
            var res = await http.PostAsJsonAsync("chat/completions", req, ct);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            logger.LogDebug("LLM complete: model={Model} prompt={PromptLength} response={ResponseLength} elapsed={ElapsedMs}ms",
                _opt.Model, prompt.Length, content.Length, sw.Elapsed.TotalMilliseconds);

            return content;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "LLM HTTP error (model={Model}, elapsed={ElapsedMs}ms)", _opt.Model, sw.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM error (model={Model}, elapsed={ElapsedMs}ms)", _opt.Model, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }
}
