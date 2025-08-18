namespace Knutr.Adapters.Slack;

public sealed class SlackOptions
{
    public bool EnableSignatureValidation { get; set; } = false;
    public string? SigningSecret { get; set; }
    public string? BotToken { get; set; }
    public string ApiBase { get; set; } = "https://slack.com/api";
}
