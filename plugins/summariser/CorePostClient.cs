using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Plugins.Summariser;

public sealed class CorePostClient(IHttpClientFactory httpFactory, IOptions<SummariserOptions> options, ILogger<CorePostClient> log)
{
    private readonly HttpClient _http = httpFactory.CreateClient("core-callback");
    private readonly string _baseUrl = options.Value.CoreBaseUrl.TrimEnd('/');

    public async Task<string?> PostMessageAsync(string channelId, string text, string? threadTs, CancellationToken ct)
    {
        try
        {
            var res = await _http.PostAsJsonAsync($"{_baseUrl}/internal/post",
                new { channelId, text, threadTs }, ct);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            var ok = json.GetProperty("ok").GetBoolean();
            if (!ok)
            {
                log.LogWarning("Core callback returned ok=false");
                return null;
            }
            return json.GetProperty("messageTs").GetString();
        }
        catch (Exception ex)
        {
            log.LogWarning("Core callback failed: {Message}", ex.Message);
            return null;
        }
    }
}
