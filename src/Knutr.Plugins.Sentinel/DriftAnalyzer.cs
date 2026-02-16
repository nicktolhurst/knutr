using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Knutr.Plugins.Sentinel;

public sealed class DriftAnalyzer(OllamaHelper ollama, ILogger<DriftAnalyzer> log, SentinelState state)
{
    // ── Topic Context ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the topic context for drift analysis. The original message always
    /// anchors the topic. If we've built up a summary from on-topic messages,
    /// that's included too for richer understanding.
    /// </summary>
    public static string BuildTopicContext(ThreadWatch watch)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Original thread message: \"{watch.OriginalMessage}\"");

        if (watch.TopicSummary.Length > 0)
            sb.AppendLine($"Understood topic so far: {watch.TopicSummary}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Re-summarizes the topic from the original message + on-topic conversation.
    /// Called periodically as on-topic messages reinforce the topic understanding.
    /// </summary>
    public async Task RefreshTopicSummary(ThreadWatch watch, List<BufferedMessage> buffer, CancellationToken ct)
    {
        var conversation = new StringBuilder();
        foreach (var msg in buffer)
            conversation.AppendLine($"- {msg.Text}");

        var prompt = $$"""
            A thread started with this message: "{{watch.OriginalMessage}}"

            The conversation so far:
            {{conversation}}
            Based on the original message and how the conversation has developed, what is this thread about?
            Reply with ONLY the topic in one short sentence. No explanation.
            """;

        log.LogDebug("Refreshing topic summary (every {Interval} on-topic messages)", state.TopicRefreshInterval);
        var response = await ollama.GenerateAsync(prompt, ct);
        var summary = CleanTopicResponse(response);

        if (summary.Length > 0)
        {
            var old = watch.TopicSummary;
            watch.TopicSummary = summary;
            watch.MessagesSinceTopicRefresh = 0;
            state.RecordTopic(watch.ChannelId, watch.ThreadTs, summary);
            log.LogInformation("Topic evolved: {Old} -> {New}",
                old.Length > 0 ? Truncate(old, SentinelDefaults.TruncateDefault) : "(none)",
                Truncate(summary, SentinelDefaults.TruncateDefault));
        }
        else
        {
            watch.MessagesSinceTopicRefresh = 0;
            log.LogDebug("Topic refresh produced no usable summary, keeping current");
        }
    }

    // ── LLM Calls ────────────────────────────────────────────────────────────

    public async Task<(double relevance, string? nudge)> AnalyzeDrift(
        string topicContext, List<BufferedMessage> buffer, string newMessage, CancellationToken ct)
    {
        var recentMessages = new StringBuilder();
        foreach (var msg in buffer.TakeLast(10))
            recentMessages.AppendLine($"- {msg.Text}");

        var prompt = $$"""
            You are monitoring a conversation thread for topic drift.

            {{topicContext}}

            Recent messages in thread:
            {{recentMessages}}
            New message: "{{newMessage}}"

            How relevant is the new message to what this thread is about?
            Consider the original message as the anchor — replies that naturally follow from it are on-topic.
            Score from 0.0 (completely off-topic) to 1.0 (clearly on-topic).

            Respond with ONLY valid JSON, nothing else:
            {"relevance": 0.0, "nudge": "friendly redirect if off-topic, empty if on-topic"}
            """;

        var response = await ollama.GenerateAsync(prompt, ct);

        var parsed = TryParseJson(response);
        if (parsed is not null)
        {
            var relevance = 1.0;
            if (parsed.Value.TryGetProperty("relevance", out var rel))
            {
                relevance = rel.ValueKind == JsonValueKind.Number
                    ? rel.GetDouble()
                    : double.TryParse(rel.GetString(), out var p) ? p : 1.0;
            }

            var nudge = parsed.Value.TryGetProperty("nudge", out var nud)
                ? nud.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(nudge))
                nudge = null;

            log.LogDebug("Parsed: relevance={Relevance:F2} nudge={Nudge}",
                relevance, nudge ?? "(none)");

            return (relevance, nudge);
        }

        log.LogWarning("Could not parse drift response: {Response}", Truncate(response, SentinelDefaults.TruncateError));
        return (1.0, null);
    }

    public async Task<string?> CheckTopicSimilarity(string channelId, string originalMessage, string excludeThreadTs, CancellationToken ct)
    {
        var history = state.GetTopicHistory(channelId)
            .Where(t => t.ThreadTs != excludeThreadTs)
            .ToList();

        if (history.Count == 0)
            return null;

        var numberedList = new StringBuilder();
        for (var i = 0; i < history.Count; i++)
            numberedList.AppendLine($"{i + 1}. {history[i].TopicSummary}");

        var prompt = $$"""
            Compare this topic against previous topics and identify if any are similar.
            New: {{originalMessage}}
            Previous:
            {{numberedList}}
            Respond with ONLY JSON: {"similar": false} or {"similar": true, "match": N}
            """;

        var response = await ollama.GenerateAsync(prompt, ct);
        var json = TryParseJson(response);

        if (json is not null && json.Value.TryGetProperty("similar", out var sim))
        {
            var isSimilar = sim.ValueKind == JsonValueKind.True
                || (sim.ValueKind == JsonValueKind.String && sim.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

            if (isSimilar && json.Value.TryGetProperty("match", out var m))
            {
                var matchIdx = (m.ValueKind == JsonValueKind.Number ? m.GetInt32() : int.TryParse(m.GetString(), out var mp) ? mp : -1) - 1;
                if (matchIdx >= 0 && matchIdx < history.Count)
                {
                    return $"This topic seems similar to a previous discussion: _{history[matchIdx].TopicSummary}_";
                }
            }
        }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string CleanTopicResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "";

        var topic = response.Trim().Trim('"');

        // Reject LLM meta-commentary
        if (topic.Length > 120
            || topic.Contains("no text", StringComparison.OrdinalIgnoreCase)
            || topic.Contains("there is", StringComparison.OrdinalIgnoreCase)
            || topic.Contains("I don't", StringComparison.OrdinalIgnoreCase)
            || topic.Contains("I cannot", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return topic;
    }

    private JsonElement? TryParseJson(string response)
    {
        var jsonStart = response.IndexOf('{');
        if (jsonStart < 0)
            return null;

        // Try progressively shorter substrings from the last } backwards,
        // because LLMs often add trailing }} or extra text after the JSON.
        var lastBrace = response.Length - 1;
        while (lastBrace >= jsonStart)
        {
            var idx = response.LastIndexOf('}', lastBrace);
            if (idx < jsonStart) break;

            var candidate = response[jsonStart..(idx + 1)];
            try
            {
                return JsonDocument.Parse(candidate).RootElement;
            }
            catch
            {
                lastBrace = idx - 1;
            }
        }

        // LLM may have truncated the response — try appending missing braces
        var fragment = response[jsonStart..];
        for (var i = 0; i < 3; i++)
        {
            fragment += "}";
            try
            {
                log.LogDebug("Recovered truncated JSON by appending {Count} brace(s)", i + 1);
                return JsonDocument.Parse(fragment).RootElement;
            }
            catch { }
        }

        log.LogWarning("No valid JSON in response: {Response}", Truncate(response, SentinelDefaults.TruncateError));
        return null;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
