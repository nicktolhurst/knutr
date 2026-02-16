using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Plugins.Summariser;

public sealed record ExporterChannel(string ChannelId, string Status, string? LastSyncTs, int MessageCount);
public sealed record ExporterMessage(string ChannelId, string MessageTs, string? ThreadTs, string UserId, string Text, string? EditedTs, DateTimeOffset ImportedAt);
public sealed record ExporterUser(string UserId, string? DisplayName, string? RealName, bool IsBot);

public sealed class ExporterClient(IHttpClientFactory httpFactory, IOptions<SummariserOptions> options, ILogger<ExporterClient> log)
{
    private readonly HttpClient _http = httpFactory.CreateClient("exporter-api");
    private readonly string _baseUrl = options.Value.ExporterBaseUrl.TrimEnd('/');

    public async Task<List<ExporterChannel>> GetChannelsAsync(CancellationToken ct)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<ExporterChannel>>($"{_baseUrl}/api/channels", ct);
            return result ?? [];
        }
        catch (Exception ex)
        {
            log.LogWarning("Failed to fetch channels from Exporter: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<List<ExporterMessage>> GetMessagesAsync(string channelId, int limit, int offset, CancellationToken ct)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<ExporterMessage>>(
                $"{_baseUrl}/api/channels/{channelId}/messages?limit={limit}&offset={offset}", ct);
            return result ?? [];
        }
        catch (Exception ex)
        {
            log.LogWarning("Failed to fetch messages from Exporter: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<ExporterUser?> GetUserAsync(string userId, CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<ExporterUser>($"{_baseUrl}/api/users/{userId}", ct);
        }
        catch (Exception ex)
        {
            log.LogDebug("Failed to fetch user {UserId}: {Message}", userId, ex.Message);
            return null;
        }
    }

    public async Task<List<ExporterMessage>> GetAllMessagesAsync(string channelId, int maxMessages, CancellationToken ct)
    {
        var all = new List<ExporterMessage>();
        var pageSize = 100;
        var offset = 0;

        while (all.Count < maxMessages)
        {
            var remaining = maxMessages - all.Count;
            var limit = Math.Min(pageSize, remaining);
            var batch = await GetMessagesAsync(channelId, limit, offset, ct);

            if (batch.Count == 0)
                break;

            all.AddRange(batch);
            offset += batch.Count;

            if (batch.Count < limit)
                break;
        }

        log.LogInformation("Fetched {Count} messages for channel {ChannelId}", all.Count, channelId);
        return all;
    }
}
