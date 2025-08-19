using Knutr.Abstractions.NL;
using Microsoft.Extensions.Configuration;

namespace Knutr.Infrastructure.Prompts;

public sealed class ConfigPromptProvider(IConfiguration cfg) : ISystemPromptProvider
{
    public string BuildSystemPrompt(string? style = null)
        => cfg["Prompts:SystemTemplate"] ?? "You are Knutr, a helpful but sarcastic assistant for Slack.";
}
