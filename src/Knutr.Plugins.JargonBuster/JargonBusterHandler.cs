namespace Knutr.Plugins.JargonBuster;

using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Knutr.Sdk;
using Microsoft.Extensions.Configuration;
using Serilog;

public sealed class JargonBusterHandler(ILogger<JargonBusterHandler> log, IConfiguration configuration) : IPluginHandler
{
    private static readonly Dictionary<string, string> Tlas = LoadTlas();
    private bool UseNlp => configuration.GetValue("JargonBuster:UseNlp", true);

    // Matches any known TLA as a whole word (case-insensitive).
    // Sorted longest-first so "CI/CD" matches before "CI".
    private static readonly Regex TlaPattern = BuildPattern();

    // Tracks which TLAs have already been explained per thread/channel to avoid repetition.
    // Key: "channelId:threadTs" for threaded messages, "channelId" for top-level.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _explained = new();

    public PluginManifest GetManifest() => new()
    {
        Name = "JargonBuster",
        Version = "1.0.0",
        Description = "Scans messages for TLAs and explains them",
        SupportsScan = true,
    };

    public Task<PluginExecuteResponse> ExecuteAsync(PluginExecuteRequest request, CancellationToken ct = default)
        => Task.FromResult(PluginExecuteResponse.Fail("JargonBuster only supports scan mode"));

    public Task<PluginExecuteResponse?> ScanAsync(PluginScanRequest request, CancellationToken ct = default)
    {
        var isReactionTriggered = request.ThreadTs?.StartsWith("_reaction_") == true;

        // For dedup, use the real thread context (strip _reaction_ prefix if present)
        var threadTs = isReactionTriggered ? request.ThreadTs!["_reaction_".Length..] : request.ThreadTs;
        var contextKey = !string.IsNullOrWhiteSpace(threadTs)
            ? $"{request.ChannelId}:{threadTs}"
            : request.ChannelId;

        var seen = _explained.GetOrAdd(contextKey, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));

        var matches = new List<(string key, string definition)>();

        foreach (Match m in TlaPattern.Matches(request.Text))
        {
            var word = m.Value;
            if (Tlas.TryGetValue(word, out var def)
                && !matches.Any(x => x.key.Equals(word, StringComparison.OrdinalIgnoreCase))
                && (isReactionTriggered || !seen.ContainsKey(word)))
                matches.Add((word, def));
        }

        if (matches.Count == 0)
            return Task.FromResult<PluginExecuteResponse?>(null);

        log.LogInformation("Matches found: [{Matches}]", string.Join(", ", matches.Select(m => m.key)));

        // Normal scan mode: react only, no text/blocks. Record TLAs as seen for dedup.
        if (!isReactionTriggered)
        {
            foreach (var (key, _) in matches)
                seen.TryAdd(key, 0);

            var reactResponse = new PluginExecuteResponse
            {
                Success = true,
                Reactions = ["knutr-teach-me"],
            };

            return Task.FromResult<PluginExecuteResponse?>(reactResponse);
        }

        // Reaction-triggered mode: return definitions for the ReactionHandler to format.
        // One TLA per line so the handler can split into separate context blocks.
        var sb = new StringBuilder();
        foreach (var (key, def) in matches)
            sb.AppendLine($"{key}: {def}");

        var response = new PluginExecuteResponse
        {
            Success = true,
            Text = sb.ToString().TrimEnd(),
            Markdown = true,
            UseNaturalLanguage = UseNlp,
            NaturalLanguageStyle = UseNlp
                ? "Rewrite each line below. Keep the abbreviation and its expansion, then add a short informative description. Do not introduce yourself. Do not add any commentary. Output exactly one line per term.\n\nExample input:\nAPI: Application Programming Interface\n\nExample output:\nAPI: Application Programming Interface - A set of protocols that allows software components to communicate.\n\nNow rewrite the following:"
                : null,
        };

        return Task.FromResult<PluginExecuteResponse?>(response);
    }

    private static Dictionary<string, string> LoadTlas()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Knutr.Plugins.JargonBuster.tlas.json")
            ?? throw new InvalidOperationException("Embedded resource tlas.json not found");

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Failed to deserialize tlas.json");

        return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static Regex BuildPattern()
    {
        var keys = Tlas.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape);
        var pattern = @"\b(" + string.Join("|", keys) + @")\b";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}
