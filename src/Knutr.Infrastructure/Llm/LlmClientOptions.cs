namespace Knutr.Infrastructure.Llm;

public sealed class LlmClientOptions
{
    public string Provider { get; set; } = "Ollama"; // or "OpenAI"
    public string? BaseUrl { get; set; }
    public string Model { get; set; } = "llama3";
    public string? ApiKey { get; set; }
}
