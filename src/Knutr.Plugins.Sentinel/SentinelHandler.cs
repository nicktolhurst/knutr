using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Knutr.Sdk;
using Microsoft.Extensions.Logging;

namespace Knutr.Plugins.Sentinel;

public sealed partial class SentinelHandler(OllamaHelper ollama, ILogger<SentinelHandler> log) : IPluginHandler
{
    private readonly SentinelState _state = new();

    public PluginManifest GetManifest() => new()
    {
        Name = "Sentinel",
        Version = "1.0.0",
        Description = "Channel focus guardian — watches threads for topic drift",
        SupportsScan = true,
        Subcommands =
        [
            new() { Name = "sentinel", Description = "Sentinel configuration and status" }
        ],
    };

    // ── Execute: /knutr sentinel <action> ────────────────────────────────────

    public Task<PluginExecuteResponse> ExecuteAsync(PluginExecuteRequest request, CancellationToken ct = default)
    {
        var args = request.Args;
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

        return Task.FromResult(action switch
        {
            "status" => HandleStatus(),
            "set" => HandleSet(args),
            "get" => HandleGet(args),
            "watch" => HandleWatchChannel(request.ChannelId, request.UserId),
            "unwatch" => HandleUnwatchChannel(request.ChannelId),
            _ => PluginExecuteResponse.EphemeralOk(
                $"Unknown sentinel command: `{action}`\n" +
                "Available: `status`, `set <key>=<value>`, `get <key>`, `watch`, `unwatch`"),
        });
    }

    private PluginExecuteResponse HandleStatus()
    {
        var config = _state.GetAllConfig();
        var sb = new StringBuilder();
        sb.AppendLine("*Sentinel Status*");
        sb.AppendLine();
        sb.AppendLine("*Configuration:*");
        foreach (var (key, value) in config)
            sb.AppendLine($"  `{key}` = `{value}`");
        sb.AppendLine();
        sb.AppendLine($"*Watched threads:* {_state.WatchedThreadCount}");
        sb.AppendLine($"*Watched channels:* {_state.WatchedChannelCount}");

        return PluginExecuteResponse.EphemeralOk(sb.ToString().TrimEnd());
    }

    private PluginExecuteResponse HandleSet(string[] args)
    {
        if (args.Length < 2)
            return PluginExecuteResponse.EphemeralOk("Usage: `sentinel set <key>=<value>`");

        var kvPart = string.Join(' ', args[1..]);
        var eqIdx = kvPart.IndexOf('=');
        if (eqIdx < 0)
            return PluginExecuteResponse.EphemeralOk("Usage: `sentinel set <key>=<value>`");

        var key = kvPart[..eqIdx].Trim();
        var value = kvPart[(eqIdx + 1)..].Trim();
        _state.SetConfig(key, value);
        log.LogInformation("Config updated: {Key}={Value}", key, value);
        return PluginExecuteResponse.EphemeralOk($"Set `{key}` = `{value}`");
    }

    private PluginExecuteResponse HandleGet(string[] args)
    {
        if (args.Length < 2)
            return PluginExecuteResponse.EphemeralOk("Usage: `sentinel get <key>`");

        var key = args[1].Trim();
        var value = _state.GetConfig(key);
        return PluginExecuteResponse.EphemeralOk($"`{key}` = `{value}`");
    }

    private PluginExecuteResponse HandleWatchChannel(string channelId, string userId)
    {
        _state.WatchChannel(channelId, userId);
        log.LogInformation("Now watching channel {Channel}", channelId);
        return PluginExecuteResponse.EphemeralOk(
            "Sentinel is now watching this channel. New threads will be auto-watched for topic drift.");
    }

    private PluginExecuteResponse HandleUnwatchChannel(string channelId)
    {
        _state.UnwatchChannel(channelId);
        log.LogInformation("Stopped watching channel {Channel}", channelId);
        return PluginExecuteResponse.EphemeralOk("Sentinel stopped watching this channel.");
    }

    // ── Scan: passive message processing ─────────────────────────────────────

    public async Task<PluginExecuteResponse?> ScanAsync(PluginScanRequest request, CancellationToken ct = default)
    {
        var text = request.Text;
        var channelId = request.ChannelId;
        var threadTs = request.ThreadTs;
        var userId = request.UserId;

        // 1. Command detection
        var cmdResult = await TryHandleCommand(text, channelId, threadTs, userId, ct);
        if (cmdResult is not null)
        {
            log.LogInformation("Command handled (suppress={Suppress})", cmdResult.SuppressMention);
            return cmdResult;
        }

        // 2. No thread context → nothing to analyze
        if (string.IsNullOrEmpty(threadTs))
            return null;

        // 3. Channel auto-watch
        if (_state.IsChannelWatched(channelId) && !_state.IsThreadWatched(channelId, threadTs))
        {
            _state.WatchThread(channelId, threadTs, text, userId);
            log.LogInformation("Auto-watching thread {ThreadTs}, original: {Text}", threadTs, Truncate(text, 60));
        }

        // 4. Skip if not watched
        var watch = _state.GetThreadWatch(channelId, threadTs);
        if (watch is null)
            return null;

        log.LogInformation("Scan: channel={Channel} thread={ThreadTs} text={Text}",
            channelId, threadTs, Truncate(text, 80));

        // 5. Buffer the message
        _state.BufferMessage(channelId, threadTs, userId, text);
        var buffer = _state.GetBuffer(channelId, threadTs);

        // 5b. Adopt first real message as the original if we started from a watch command
        if (watch.OriginalMessage == "(watching from here)")
        {
            watch.OriginalMessage = text;
            log.LogInformation("Adopted original message: {Text}", Truncate(text, 60));
        }

        // 6. Skip analysis until we have enough context
        if (buffer.Count < 3)
        {
            log.LogInformation("Buffering ({Count}/3 before analysis)", buffer.Count);
            return null;
        }

        // 7. Build topic context and analyze drift
        var topicContext = BuildTopicContext(watch);
        log.LogInformation("Analyzing: original={Original} topic={Topic} buffer={Count} threshold={Threshold}",
            Truncate(watch.OriginalMessage, 40),
            watch.TopicSummary.Length > 0 ? Truncate(watch.TopicSummary, 40) : "(building)",
            buffer.Count, _state.Threshold);

        var (relevance, nudge) = await AnalyzeDrift(topicContext, buffer, text, ct);

        log.LogInformation("Result: relevance={Relevance:F2} threshold={Threshold} nudge={Nudge}",
            relevance, _state.Threshold, nudge is not null ? "yes" : "no");

        // 8. On-topic: reinforce topic understanding
        if (relevance >= _state.Threshold)
        {
            log.LogInformation("On-topic ({Relevance:F2} >= {Threshold})", relevance, _state.Threshold);
            watch.MessagesSinceTopicRefresh++;

            // Periodically re-summarize the topic from conversation context
            if (watch.MessagesSinceTopicRefresh >= _state.TopicRefreshInterval)
            {
                await RefreshTopicSummary(watch, buffer, ct);
            }

            return null;
        }

        // 9. Playful mode
        if (_state.Playful && relevance >= _state.PlayfulThreshold)
        {
            log.LogInformation("Playful reaction ({Relevance:F2} in playful range)", relevance);
            return new PluginExecuteResponse
            {
                Success = true,
                Text = "",
                Reactions = ["eyes"],
            };
        }

        // 10. Off-topic — nudge
        var nudgeText = nudge ?? "Looks like this might be drifting off-topic. Want to start a new thread for this?";
        log.LogInformation("Drift! relevance={Relevance:F2} < threshold={Threshold}{Default}",
            relevance, _state.Threshold, nudge is null ? " (default nudge)" : "");
        return new PluginExecuteResponse
        {
            Success = true,
            Text = nudgeText,
            Markdown = true,
            SuppressMention = true,
        };
    }

    // ── Topic Context ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the topic context for drift analysis. The original message always
    /// anchors the topic. If we've built up a summary from on-topic messages,
    /// that's included too for richer understanding.
    /// </summary>
    private static string BuildTopicContext(ThreadWatch watch)
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
    private async Task RefreshTopicSummary(ThreadWatch watch, List<BufferedMessage> buffer, CancellationToken ct)
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

        log.LogInformation("Refreshing topic summary (every {Interval} on-topic messages)", _state.TopicRefreshInterval);
        var response = await ollama.GenerateAsync(prompt, ct);
        var summary = CleanTopicResponse(response);

        if (summary.Length > 0)
        {
            var old = watch.TopicSummary;
            watch.TopicSummary = summary;
            watch.MessagesSinceTopicRefresh = 0;
            _state.RecordTopic(watch.ChannelId, watch.ThreadTs, summary);
            log.LogInformation("Topic evolved: {Old} -> {New}",
                old.Length > 0 ? Truncate(old, 60) : "(none)",
                Truncate(summary, 60));
        }
        else
        {
            watch.MessagesSinceTopicRefresh = 0;
            log.LogInformation("Topic refresh produced no usable summary, keeping current");
        }
    }

    // ── Command Detection ────────────────────────────────────────────────────

    private async Task<PluginExecuteResponse?> TryHandleCommand(string text, string channelId, string? threadTs, string userId, CancellationToken ct)
    {
        var lower = text.ToLowerInvariant().Trim();

        if (WatchPattern().IsMatch(lower))
        {
            log.LogInformation("Watch command detected");

            if (string.IsNullOrEmpty(threadTs))
            {
                return new PluginExecuteResponse
                {
                    Success = true,
                    Text = "Use this command inside a thread to watch it, or use `/knutr sentinel watch` to watch the channel.",
                    Ephemeral = true,
                    SuppressMention = true,
                };
            }

            if (_state.IsThreadWatched(channelId, threadTs))
            {
                return new PluginExecuteResponse
                {
                    Success = true,
                    Text = "Already watching this thread.",
                    Ephemeral = true,
                    SuppressMention = true,
                };
            }

            // Use the first buffered message as the original context, or a placeholder.
            // The topic will build naturally from the conversation.
            var buffer = _state.GetBuffer(channelId, threadTs);
            var originalMessage = buffer.Count > 0 ? buffer[0].Text : "(watching from here)";

            _state.WatchThread(channelId, threadTs, originalMessage, userId);
            log.LogInformation("Watching thread {ThreadTs}, original: {Original}", threadTs, Truncate(originalMessage, 60));

            var similarNote = await CheckTopicSimilarity(channelId, originalMessage, threadTs, ct);
            var response = "Sentinel is now watching this thread for topic drift.";
            if (!string.IsNullOrEmpty(similarNote))
                response += $"\n{similarNote}";

            return new PluginExecuteResponse
            {
                Success = true,
                Text = response,
                Ephemeral = true,
                SuppressMention = true,
            };
        }

        if (UnwatchPattern().IsMatch(lower))
        {
            log.LogInformation("Unwatch command detected");

            if (string.IsNullOrEmpty(threadTs))
            {
                return new PluginExecuteResponse
                {
                    Success = true,
                    Text = "Use this command inside a thread to stop watching it.",
                    Ephemeral = true,
                    SuppressMention = true,
                };
            }

            _state.UnwatchThread(channelId, threadTs);
            log.LogInformation("Stopped watching thread {ThreadTs}", threadTs);
            return new PluginExecuteResponse
            {
                Success = true,
                Text = "Sentinel stopped watching this thread.",
                Ephemeral = true,
                SuppressMention = true,
            };
        }

        return null;
    }

    [GeneratedRegex(@"(?:<@\w+>|@\w+)\s+(watch\s+this\s+thread|sentinel\s+watch)", RegexOptions.IgnoreCase)]
    private static partial Regex WatchPattern();

    [GeneratedRegex(@"(?:<@\w+>|@\w+)\s+(stop\s+watching|sentinel\s+unwatch)", RegexOptions.IgnoreCase)]
    private static partial Regex UnwatchPattern();

    // ── LLM Calls ────────────────────────────────────────────────────────────

    private async Task<(double relevance, string? nudge)> AnalyzeDrift(
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

            log.LogInformation("Parsed: relevance={Relevance:F2} nudge={Nudge}",
                relevance, nudge ?? "(none)");

            return (relevance, nudge);
        }

        log.LogWarning("Could not parse drift response: {Response}", Truncate(response, 200));
        return (1.0, null);
    }

    private async Task<string?> CheckTopicSimilarity(string channelId, string originalMessage, string excludeThreadTs, CancellationToken ct)
    {
        var history = _state.GetTopicHistory(channelId)
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
                log.LogInformation("Recovered truncated JSON by appending {Count} brace(s)", i + 1);
                return JsonDocument.Parse(fragment).RootElement;
            }
            catch { }
        }

        log.LogWarning("No valid JSON in response: {Response}", Truncate(response, 200));
        return null;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
