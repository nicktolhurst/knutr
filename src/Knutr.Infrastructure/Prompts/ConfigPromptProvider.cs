using Knutr.Abstractions.NL;
using Microsoft.Extensions.Configuration;

namespace Knutr.Infrastructure.Prompts;

public sealed class ConfigPromptProvider : ISystemPromptProvider
{
    private readonly IConfiguration _cfg;
    public ConfigPromptProvider(IConfiguration cfg) => _cfg = cfg;
    public string BuildSystemPrompt(string? style = null)
        => _cfg["Prompts:SystemTemplate"] ?? "You are Knutr, a helpful assistant for Slack.";
}
