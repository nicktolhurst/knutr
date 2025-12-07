using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Knutr.Abstractions.NL;
using Microsoft.Extensions.Options;

namespace Knutr.Infrastructure.Llm;

public sealed class OpenAIChatClient(HttpClient http, IOptions<LlmClientOptions> opt, LlmMetrics metrics) : ILlmClient
{
    private static readonly ActivitySource Activity = new("Knutr.Infrastructure.Llm");
    private readonly LlmClientOptions _opt = opt.Value;
    private const string ProviderName = "openai";

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

            activity?.SetTag("llm.response_length", content.Length);

            // Extract token counts from OpenAI response
            if (json.TryGetProperty("usage", out var usage))
            {
                var inputTokens = usage.GetProperty("prompt_tokens").GetInt64();
                var outputTokens = usage.GetProperty("completion_tokens").GetInt64();
                metrics.RecordTokens(ProviderName, _opt.Model, inputTokens, outputTokens);
                activity?.SetTag("llm.input_tokens", inputTokens);
                activity?.SetTag("llm.output_tokens", outputTokens);
            }

            return content;
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
