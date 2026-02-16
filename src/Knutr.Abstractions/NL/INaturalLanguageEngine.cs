namespace Knutr.Abstractions.NL;

using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;

public interface INaturalLanguageEngine
{
    Task<Reply> GenerateAsync(NlMode mode, string? text = null, string? style = null, object? context = null, CancellationToken ct = default);
}
