using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Knutr.Abstractions.NL;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Infrastructure.Llm;

public sealed class OllamaClient(HttpClient http, IOptions<LlmClientOptions> opt, ILogger<OllamaClient> logger) : ILlmClient
{
    private readonly LlmClientOptions _opt = opt.Value;

    public async Task<string> CompleteAsync(string system, string prompt, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var res = await http.PostAsJsonAsync("/api/generate", new { model = _opt.Model, prompt = $"{system}\n\n{prompt}", stream = false }, ct);

            if (!res.IsSuccessStatusCode)
            {
                var statusCode = (int)res.StatusCode;
                var errorBody = await res.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Received {StatusCode} from LLM (model={Model}): {Error}",
                    statusCode, _opt.Model, errorBody);
                throw new HttpRequestException($"LLM returned {statusCode}: {errorBody}", null, res.StatusCode);
            }

            var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var response = json.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";

            logger.LogDebug("LLM complete: model={Model} prompt={PromptLength} response={ResponseLength} elapsed={ElapsedMs}ms",
                _opt.Model, prompt.Length, response.Length, sw.Elapsed.TotalMilliseconds);

            return response;
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
