namespace Knutr.Abstractions.NL;

public interface ILlmClient
{
    Task<string> CompleteAsync(string system, string prompt, CancellationToken ct = default);
}
