using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Knutr.Abstractions.NL;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Infrastructure.Llm;

public sealed class OllamaClient(HttpClient http, IOptions<LlmClientOptions> opt, LlmMetrics metrics, ILogger<OllamaClient> logger) : ILlmClient
{
    private static readonly ActivitySource Activity = new("Knutr.Infrastructure.Llm");
    private readonly LlmClientOptions _opt = opt.Value;
    private const string ProviderName = "ollama";

    public async Task<string> CompleteAsync(string system, string prompt, CancellationToken ct = default)
    {
        using var activity = Activity.StartActivity("llm_complete");
        activity?.SetTag("llm.provider", ProviderName);
        activity?.SetTag("llm.model", _opt.Model);
        activity?.SetTag("llm.prompt_length", prompt.Length);

        var sw = Stopwatch.StartNew();
        var outcome = "success";

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

            activity?.SetTag("llm.response_length", response.Length);

            // Try to extract token counts if available in response
            if (json.TryGetProperty("prompt_eval_count", out var inputTokens) &&
                json.TryGetProperty("eval_count", out var outputTokens))
            {
                metrics.RecordTokens(ProviderName, _opt.Model, inputTokens.GetInt64(), outputTokens.GetInt64());
                activity?.SetTag("llm.input_tokens", inputTokens.GetInt64());
                activity?.SetTag("llm.output_tokens", outputTokens.GetInt64());
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            outcome = "error";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            metrics.RecordError(ProviderName, _opt.Model, "http_error");
            throw;
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == ct)
        {
            outcome = "cancelled";
            activity?.SetStatus(ActivityStatusCode.Error, "Request cancelled");
            throw;
        }
        catch (Exception ex)
        {
            outcome = "error";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            metrics.RecordError(ProviderName, _opt.Model, ex.GetType().Name);
            throw;
        }
        finally
        {
            metrics.RecordRequest(ProviderName, _opt.Model, outcome, sw.Elapsed.TotalMilliseconds);
        }
    }
}
