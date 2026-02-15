namespace Knutr.Adapters.Slack;

public sealed class SlackOptions
{
    public bool EnableSignatureValidation { get; set; } = false;
    public string? SigningSecret { get; set; }
    public string? BotToken { get; set; }
    public string ApiBase { get; set; } = "https://slack.com/api";

    // Derived URL helpers to avoid repeated string interpolation
    public string ChatPostMessageUrl => $"{ApiBase}/chat.postMessage";
    public string ChatUpdateUrl => $"{ApiBase}/chat.update";
    public string ChatPostEphemeralUrl => $"{ApiBase}/chat.postEphemeral";
    public string ConversationsOpenUrl => $"{ApiBase}/conversations.open";
    public string ReactionsAddUrl => $"{ApiBase}/reactions.add";
}
