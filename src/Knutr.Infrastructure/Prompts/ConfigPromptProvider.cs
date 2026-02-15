using Knutr.Abstractions.NL;
using Microsoft.Extensions.Configuration;

namespace Knutr.Infrastructure.Prompts;

public sealed class ConfigPromptProvider(IConfiguration cfg) : ISystemPromptProvider
{
    public string BuildSystemPrompt(string? style = null)
        => style ?? cfg["Prompts:SystemTemplate"] ?? "You are Knutr, a helpful and professional assistant for Slack. Be concise, clear, and friendly.";
}
