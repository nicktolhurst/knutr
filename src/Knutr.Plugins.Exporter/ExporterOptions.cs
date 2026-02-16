namespace Knutr.Plugins.Exporter;

public sealed class ExporterOptions
{
    public int PollIntervalMinutes { get; set; } = 5;
    public int RateLimitDelayMs { get; set; } = 1200;
    public int PageSize { get; set; } = 200;
}

public sealed class ExporterSlackOptions
{
    public string? BotToken { get; set; }
    public string ApiBase { get; set; } = "https://slack.com/api";

    public string ConversationsHistoryUrl => $"{ApiBase}/conversations.history";
    public string ConversationsRepliesUrl => $"{ApiBase}/conversations.replies";
    public string UsersInfoUrl => $"{ApiBase}/users.info";
}
