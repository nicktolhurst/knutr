using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Plugins.Summariser;

public sealed class SummaryService(
    ExporterClient exporter,
    OllamaHelper ollama,
    CorePostClient corePost,
    IOptions<SummariserOptions> options,
    ILogger<SummaryService> log)
{
    private readonly SummariserOptions _opts = options.Value;

    public async Task GenerateAndPostAsync(string channelId, string preset, string responseChannelId, string? threadTs, string userId)
    {
        try
        {
            log.LogInformation("Starting {Preset} summary for channel {ChannelId} (requested by {UserId})", preset, channelId, userId);

            // 1. Fetch messages
            var messages = await exporter.GetAllMessagesAsync(channelId, _opts.MaxMessages, CancellationToken.None);
            if (messages.Count == 0)
            {
                await PostResult(responseChannelId, "No exported messages found for this channel.", threadTs);
                return;
            }

            // 2. Resolve user display names
            var userNames = new Dictionary<string, string>();
            var uniqueUserIds = messages.Select(m => m.UserId).Distinct().ToList();
            foreach (var uid in uniqueUserIds)
            {
                if (userNames.ContainsKey(uid)) continue;
                var user = await exporter.GetUserAsync(uid, CancellationToken.None);
                userNames[uid] = user?.DisplayName ?? user?.RealName ?? uid;
            }

            // 3. Format transcript
            var transcript = FormatTranscript(messages, userNames);
            log.LogInformation("Transcript: {Chars} chars from {Count} messages", transcript.Length, messages.Count);

            // 4. Generate summary
            string summary;
            if (transcript.Length <= _opts.MaxPromptChars)
            {
                var prompt = BuildPresetPrompt(preset, transcript);
                summary = await ollama.GenerateAsync(prompt, CancellationToken.None);
            }
            else
            {
                summary = await GenerateChunkedSummary(messages, userNames, preset);
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                await PostResult(responseChannelId, "Sorry, I wasn't able to generate a summary. The LLM returned an empty response.", threadTs);
                return;
            }

            // 5. Post result
            var posted = await PostResult(responseChannelId, summary, threadTs);
            if (posted)
                log.LogInformation("Posted {Preset} summary ({Chars} chars) for channel {ChannelId}", preset, summary.Length, channelId);
            else
                log.LogWarning("Failed to post {Preset} summary for channel {ChannelId} — callback returned no messageTs", preset, channelId);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to generate {Preset} summary for channel {ChannelId}", preset, channelId);
            try
            {
                await PostResult(responseChannelId, $"Sorry, something went wrong generating the summary: {ex.Message}", threadTs);
            }
            catch (Exception postEx)
            {
                log.LogError(postEx, "Failed to post error message");
            }
        }
    }

    private async Task<string> GenerateChunkedSummary(List<ExporterMessage> messages, Dictionary<string, string> userNames, string preset)
    {
        var chunks = messages.Chunk(_opts.ChunkSize).ToList();
        log.LogInformation("Using chunked approach: {ChunkCount} chunks of up to {ChunkSize} messages", chunks.Count, _opts.ChunkSize);

        var chunkSummaries = new List<string>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkTranscript = FormatTranscript(chunks[i].ToList(), userNames);
            var chunkPrompt = $"""
                Summarise the following section of a Slack channel conversation.
                Focus on: decisions made, tasks discussed, problems raised, and any deferred work.
                Keep the summary concise but preserve key details and who said what.

                {chunkTranscript}
                """;

            var chunkSummary = await ollama.GenerateAsync(chunkPrompt, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(chunkSummary))
            {
                chunkSummaries.Add(chunkSummary);
                log.LogDebug("Chunk {Index}/{Total}: {Chars} chars", i + 1, chunks.Count, chunkSummary.Length);
            }
        }

        if (chunkSummaries.Count == 0)
            return "";

        var combinedSummaries = string.Join("\n\n---\n\n", chunkSummaries);
        var presetInstructions = GetPresetInstructions(preset);
        var mergePrompt = $"""
            {presetInstructions}

            Here are summaries of different sections of the conversation:
            {combinedSummaries}

            Combine these into a single coherent summary. Deduplicate and merge related items.
            Format the output as a well-structured Slack message using *bold* for headings and bullet points.
            """;

        return await ollama.GenerateAsync(mergePrompt, CancellationToken.None);
    }

    private static string FormatTranscript(List<ExporterMessage> messages, Dictionary<string, string> userNames)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            var ts = FormatTimestamp(msg.MessageTs);
            var name = userNames.GetValueOrDefault(msg.UserId, msg.UserId);
            sb.AppendLine($"[{ts}] @{name}: {msg.Text}");
        }
        return sb.ToString();
    }

    private static string FormatTimestamp(string messageTs)
    {
        try
        {
            var epochStr = messageTs.Split('.')[0];
            var epoch = long.Parse(epochStr);
            var dto = DateTimeOffset.FromUnixTimeSeconds(epoch);
            return dto.ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            return messageTs;
        }
    }

    private static string BuildPresetPrompt(string preset, string transcript)
    {
        return preset switch
        {
            "lessons" => $"""
                You are analysing a Slack channel conversation from a software project.

                Here is the conversation transcript:
                {transcript}

                Based on this conversation, produce a structured summary of:
                1. Key architectural decisions that were made, with approximate dates
                2. Lessons learned — what went well and what didn't
                3. Notable technical challenges and how they were resolved

                Format the output as a well-structured Slack message using *bold* for headings and bullet points. Include dates where identifiable from the timestamps.
                """,
            "backlog" => $"""
                You are analysing a Slack channel conversation from a software project.

                Here is the conversation transcript:
                {transcript}

                Identify all tasks, features, or work items that were explicitly:
                - Deprioritised or deferred ("we'll do this later", "parking this for now", etc.)
                - Descoped from the current release
                - Acknowledged as technical debt

                For each item, include:
                - What the task was
                - Who mentioned it and approximate date
                - The reason it was deprioritised (if stated)

                Format the output as a well-structured Slack message using *bold* for headings and bullet points.
                """,
            _ => $"Summarise the following conversation:\n\n{transcript}",
        };
    }

    private static string GetPresetInstructions(string preset)
    {
        return preset switch
        {
            "lessons" => """
                You are analysing a Slack channel conversation from a software project.
                Based on the conversation summaries below, produce a structured summary of:
                1. Key architectural decisions that were made, with approximate dates
                2. Lessons learned — what went well and what didn't
                3. Notable technical challenges and how they were resolved
                """,
            "backlog" => """
                You are analysing a Slack channel conversation from a software project.
                Identify all tasks, features, or work items that were explicitly:
                - Deprioritised or deferred
                - Descoped from the current release
                - Acknowledged as technical debt
                For each item, include what the task was, who mentioned it, approximate date, and the reason it was deprioritised.
                """,
            _ => "Summarise the following conversation sections into a coherent summary.",
        };
    }

    private async Task<bool> PostResult(string channelId, string text, string? threadTs)
    {
        if (threadTs is not null)
        {
            var ts = await corePost.PostMessageAsync(channelId, text, threadTs, CancellationToken.None);
            return ts is not null;
        }
        else
        {
            var parentTs = await corePost.PostMessageAsync(channelId, text, null, CancellationToken.None);
            return parentTs is not null;
        }
    }
}
