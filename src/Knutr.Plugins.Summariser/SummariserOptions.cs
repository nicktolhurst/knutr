namespace Knutr.Plugins.Summariser;

public sealed class SummariserOptions
{
    public int MaxMessages { get; set; } = 500;
    public int ChunkSize { get; set; } = 100;
    public int MaxPromptChars { get; set; } = 12000;
    public string ExporterBaseUrl { get; set; } = "http://knutr-plugin-exporter.knutr.svc.cluster.local";
    public string CoreBaseUrl { get; set; } = "http://knutr-core.knutr.svc.cluster.local";
}

public sealed class SummariserOllamaOptions
{
    public string Url { get; set; } = "http://ollama.knutr.svc.cluster.local:11434";
    public string Model { get; set; } = "llama3.2:1b";
}

public sealed class SummariserSlackOptions
{
    public string? BotToken { get; set; }
}
