namespace Knutr.Abstractions.NL;

public interface ISystemPromptProvider
{
    string BuildSystemPrompt(string? style = null);
}
