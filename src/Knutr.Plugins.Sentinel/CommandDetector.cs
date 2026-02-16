using System.Text.RegularExpressions;
using Knutr.Sdk;
using Microsoft.Extensions.Logging;

namespace Knutr.Plugins.Sentinel;

public sealed partial class CommandDetector(SentinelState state, DriftAnalyzer drift, ILogger<CommandDetector> log)
{
    public async Task<PluginExecuteResponse?> TryHandleCommand(string text, string channelId, string? threadTs, string userId, CancellationToken ct)
    {
        var lower = text.ToLowerInvariant().Trim();

        if (WatchPattern().IsMatch(lower))
        {
            log.LogDebug("Watch command detected");

            if (string.IsNullOrWhiteSpace(threadTs))
            {
                return new PluginExecuteResponse
                {
                    Success = true,
                    Text = "Use this command inside a thread to watch it, or use `/knutr sentinel watch` to watch the channel.",
                    Ephemeral = true,
                    SuppressMention = true,
                };
            }

            if (state.IsThreadWatched(channelId, threadTs))
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
            var buffer = state.GetBuffer(channelId, threadTs);
            var originalMessage = buffer.Count > 0 ? buffer[0].Text : "(watching from here)";

            state.WatchThread(channelId, threadTs, originalMessage, userId);
            log.LogInformation("Watching thread {ThreadTs}, original: {Original}", threadTs, Truncate(originalMessage));

            var similarNote = await drift.CheckTopicSimilarity(channelId, originalMessage, threadTs, ct);
            var response = "Sentinel is now watching this thread for topic drift.";
            if (!string.IsNullOrWhiteSpace(similarNote))
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
            log.LogDebug("Unwatch command detected");

            if (string.IsNullOrWhiteSpace(threadTs))
            {
                return new PluginExecuteResponse
                {
                    Success = true,
                    Text = "Use this command inside a thread to stop watching it.",
                    Ephemeral = true,
                    SuppressMention = true,
                };
            }

            state.UnwatchThread(channelId, threadTs);
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

    private static string Truncate(string text, int maxLength = SentinelDefaults.TruncateDefault)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
