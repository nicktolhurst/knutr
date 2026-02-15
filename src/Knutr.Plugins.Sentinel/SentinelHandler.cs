using System.Text;
using Knutr.Sdk;
using Microsoft.Extensions.Logging;

namespace Knutr.Plugins.Sentinel;

public sealed class SentinelHandler(
    DriftAnalyzer drift,
    CommandDetector commands,
    SentinelState state,
    ILogger<SentinelHandler> log) : IPluginHandler
{
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
        var config = state.GetAllConfig();
        var sb = new StringBuilder();
        sb.AppendLine("*Sentinel Status*");
        sb.AppendLine();
        sb.AppendLine("*Configuration:*");
        foreach (var (key, value) in config)
            sb.AppendLine($"  `{key}` = `{value}`");
        sb.AppendLine();
        sb.AppendLine($"*Watched threads:* {state.WatchedThreadCount}");
        sb.AppendLine($"*Watched channels:* {state.WatchedChannelCount}");

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
        state.SetConfig(key, value);
        log.LogInformation("Config updated: {Key}={Value}", key, value);
        return PluginExecuteResponse.EphemeralOk($"Set `{key}` = `{value}`");
    }

    private PluginExecuteResponse HandleGet(string[] args)
    {
        if (args.Length < 2)
            return PluginExecuteResponse.EphemeralOk("Usage: `sentinel get <key>`");

        var key = args[1].Trim();
        var value = state.GetConfig(key);
        return PluginExecuteResponse.EphemeralOk($"`{key}` = `{value}`");
    }

    private PluginExecuteResponse HandleWatchChannel(string channelId, string userId)
    {
        state.WatchChannel(channelId, userId);
        log.LogInformation("Now watching channel {Channel}", channelId);
        return PluginExecuteResponse.EphemeralOk(
            "Sentinel is now watching this channel. New threads will be auto-watched for topic drift.");
    }

    private PluginExecuteResponse HandleUnwatchChannel(string channelId)
    {
        state.UnwatchChannel(channelId);
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
        var cmdResult = await commands.TryHandleCommand(text, channelId, threadTs, userId, ct);
        if (cmdResult is not null)
        {
            log.LogDebug("Command handled (suppress={Suppress})", cmdResult.SuppressMention);
            return cmdResult;
        }

        // 2. No thread context → nothing to analyze
        if (string.IsNullOrWhiteSpace(threadTs))
            return null;

        // 3. Channel auto-watch
        if (state.IsChannelWatched(channelId) && !state.IsThreadWatched(channelId, threadTs))
        {
            state.WatchThread(channelId, threadTs, text, userId);
            log.LogInformation("Auto-watching thread {ThreadTs}, original: {Text}", threadTs, Truncate(text, SentinelDefaults.TruncateDefault));
        }

        // 4. Skip if not watched
        var watch = state.GetThreadWatch(channelId, threadTs);
        if (watch is null)
            return null;

        log.LogDebug("Scan: channel={ChannelId} thread={ThreadTs} text={Text}",
            channelId, threadTs, Truncate(text, SentinelDefaults.TruncateLong));

        // 5. Buffer the message
        state.BufferMessage(channelId, threadTs, userId, text);
        var buffer = state.GetBuffer(channelId, threadTs);

        // 5b. Adopt first real message as the original if we started from a watch command
        if (watch.OriginalMessage == "(watching from here)")
        {
            watch.OriginalMessage = text;
            log.LogDebug("Adopted original message: {Text}", Truncate(text, SentinelDefaults.TruncateDefault));
        }

        // 6. Skip analysis until we have enough context
        if (buffer.Count < SentinelDefaults.MinBufferBeforeAnalysis)
        {
            log.LogDebug("Buffering ({Count}/{Min} before analysis)", buffer.Count, SentinelDefaults.MinBufferBeforeAnalysis);
            return null;
        }

        // 7. Build topic context and analyze drift
        var topicContext = DriftAnalyzer.BuildTopicContext(watch);
        log.LogDebug("Analyzing: original={Original} topic={Topic} buffer={Count} threshold={Threshold}",
            Truncate(watch.OriginalMessage, SentinelDefaults.TruncateShort),
            watch.TopicSummary.Length > 0 ? Truncate(watch.TopicSummary, SentinelDefaults.TruncateShort) : "(building)",
            buffer.Count, state.Threshold);

        var (relevance, nudge) = await drift.AnalyzeDrift(topicContext, buffer, text, ct);

        log.LogDebug("Result: relevance={Relevance:F2} threshold={Threshold} nudge={Nudge}",
            relevance, state.Threshold, nudge is not null ? "yes" : "no");

        // 8. On-topic: reinforce topic understanding
        if (relevance >= state.Threshold)
        {
            log.LogDebug("On-topic ({Relevance:F2} >= {Threshold})", relevance, state.Threshold);
            watch.MessagesSinceTopicRefresh++;

            // Periodically re-summarize the topic from conversation context
            if (watch.MessagesSinceTopicRefresh >= state.TopicRefreshInterval)
            {
                await drift.RefreshTopicSummary(watch, buffer, ct);
            }

            return null;
        }

        // 9. Playful mode
        if (state.Playful && relevance >= state.PlayfulThreshold)
        {
            log.LogDebug("Playful reaction ({Relevance:F2} in playful range)", relevance);
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
            relevance, state.Threshold, nudge is null ? " (default nudge)" : "");
        return new PluginExecuteResponse
        {
            Success = true,
            Text = nudgeText,
            Markdown = true,
            SuppressMention = true,
        };
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
