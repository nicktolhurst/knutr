using System.Collections.Concurrent;

namespace Knutr.Plugins.Sentinel;

public sealed class ThreadWatch
{
    public required string ChannelId { get; init; }
    public required string ThreadTs { get; init; }
    public required string WatcherUserId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// The original message that started the thread or was present when watch began.
    /// This anchors topic understanding — highest weight in drift detection.
    /// </summary>
    public required string OriginalMessage { get; set; }

    /// <summary>
    /// Evolving topic summary built from the conversation so far.
    /// Starts empty and grows as on-topic messages reinforce understanding.
    /// </summary>
    public string TopicSummary { get; set; } = "";

    /// <summary>
    /// How many messages have been analyzed since the last topic refresh.
    /// Used to decide when to re-summarize the topic.
    /// </summary>
    public int MessagesSinceTopicRefresh { get; set; }
}

public sealed record ChannelWatch(string ChannelId, string WatcherUserId, DateTimeOffset StartedAt);

public sealed record BufferedMessage(string UserId, string Text, DateTimeOffset Timestamp);

public sealed record TopicRecord(string ThreadTs, string TopicSummary, DateTimeOffset RecordedAt);

public sealed class SentinelState
{
    private readonly ConcurrentDictionary<string, ThreadWatch> _threadWatches = new();
    private readonly ConcurrentDictionary<string, ChannelWatch> _channelWatches = new();
    private readonly ConcurrentDictionary<string, List<BufferedMessage>> _messageBuffers = new();
    private readonly ConcurrentDictionary<string, List<TopicRecord>> _topicHistory = new();
    private readonly ConcurrentDictionary<string, string> _config = new(
        new Dictionary<string, string>
        {
            ["threshold"] = "0.7",
            ["playful"] = "false",
            ["playful_threshold"] = "0.5",
            ["buffer_size"] = "20",
            ["topic_refresh_interval"] = "5",
        });

    // -- Config --

    public string GetConfig(string key)
        => _config.TryGetValue(key, out var val) ? val : "(not set)";

    public void SetConfig(string key, string value)
        => _config[key] = value;

    public IReadOnlyDictionary<string, string> GetAllConfig()
        => _config.ToDictionary(kv => kv.Key, kv => kv.Value);

    public double Threshold
        => double.TryParse(GetConfig("threshold"), out var v) ? v : 0.7;

    public bool Playful
        => bool.TryParse(GetConfig("playful"), out var v) && v;

    public double PlayfulThreshold
        => double.TryParse(GetConfig("playful_threshold"), out var v) ? v : 0.5;

    public int BufferSize
        => int.TryParse(GetConfig("buffer_size"), out var v) ? v : 20;

    public int TopicRefreshInterval
        => int.TryParse(GetConfig("topic_refresh_interval"), out var v) ? v : 5;

    // -- Thread Watches --

    private static string ThreadKey(string channelId, string threadTs)
        => $"{channelId}:{threadTs}";

    public void WatchThread(string channelId, string threadTs, string originalMessage, string userId)
    {
        var key = ThreadKey(channelId, threadTs);
        _threadWatches[key] = new ThreadWatch
        {
            ChannelId = channelId,
            ThreadTs = threadTs,
            OriginalMessage = originalMessage,
            WatcherUserId = userId,
            StartedAt = DateTimeOffset.UtcNow,
        };
    }

    public bool UnwatchThread(string channelId, string threadTs)
        => _threadWatches.TryRemove(ThreadKey(channelId, threadTs), out _);

    public bool IsThreadWatched(string channelId, string threadTs)
        => _threadWatches.ContainsKey(ThreadKey(channelId, threadTs));

    public ThreadWatch? GetThreadWatch(string channelId, string threadTs)
        => _threadWatches.TryGetValue(ThreadKey(channelId, threadTs), out var w) ? w : null;

    public int WatchedThreadCount => _threadWatches.Count;

    // -- Channel Watches --

    public void WatchChannel(string channelId, string userId)
        => _channelWatches[channelId] = new ChannelWatch(channelId, userId, DateTimeOffset.UtcNow);

    public bool UnwatchChannel(string channelId)
        => _channelWatches.TryRemove(channelId, out _);

    public bool IsChannelWatched(string channelId)
        => _channelWatches.ContainsKey(channelId);

    public int WatchedChannelCount => _channelWatches.Count;

    // -- Message Buffers --

    public void BufferMessage(string channelId, string threadTs, string userId, string text)
    {
        var key = ThreadKey(channelId, threadTs);
        var buffer = _messageBuffers.GetOrAdd(key, _ => new List<BufferedMessage>());
        lock (buffer)
        {
            buffer.Add(new BufferedMessage(userId, text, DateTimeOffset.UtcNow));
            while (buffer.Count > BufferSize)
                buffer.RemoveAt(0);
        }
    }

    public List<BufferedMessage> GetBuffer(string channelId, string threadTs)
    {
        var key = ThreadKey(channelId, threadTs);
        if (!_messageBuffers.TryGetValue(key, out var buffer)) return [];
        lock (buffer)
        {
            return [.. buffer];
        }
    }

    // -- Topic History --

    public void RecordTopic(string channelId, string threadTs, string topicSummary)
    {
        var list = _topicHistory.GetOrAdd(channelId, _ => new List<TopicRecord>());
        lock (list)
        {
            // Update existing record for this thread or add new
            var existing = list.FindIndex(t => t.ThreadTs == threadTs);
            if (existing >= 0)
                list[existing] = new TopicRecord(threadTs, topicSummary, DateTimeOffset.UtcNow);
            else
                list.Add(new TopicRecord(threadTs, topicSummary, DateTimeOffset.UtcNow));
        }
    }

    public List<TopicRecord> GetTopicHistory(string channelId)
    {
        if (!_topicHistory.TryGetValue(channelId, out var list)) return [];
        lock (list)
        {
            return [.. list];
        }
    }
}
