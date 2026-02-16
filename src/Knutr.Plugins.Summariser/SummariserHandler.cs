using System.Text.RegularExpressions;
using Knutr.Sdk;
using Microsoft.Extensions.Logging;

namespace Knutr.Plugins.Summariser;

public sealed partial class SummariserHandler(
    SummaryService summaryService,
    ILogger<SummariserHandler> log) : IPluginHandler
{
    private static readonly string[] ValidPresets = ["lessons", "backlog"];

    public PluginManifest GetManifest() => new()
    {
        Name = "Summariser",
        Version = "1.0.0",
        Description = "Generates structured summaries from exported channel conversations",
        Subcommands =
        [
            new() { Name = "summarise", Description = "Summarise channel conversations" }
        ],
        Dependencies = ["exporter"],
    };

    public Task<PluginExecuteResponse> ExecuteAsync(PluginExecuteRequest request, CancellationToken ct = default)
    {
        var args = request.Args;
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "";

        if (!ValidPresets.Contains(action))
        {
            return Task.FromResult(PluginExecuteResponse.EphemeralOk(
                "*Summariser — Usage*\n" +
                "  `/knutr summarise lessons` — Lessons learned for this channel\n" +
                "  `/knutr summarise lessons #channel` — Lessons for a specific channel\n" +
                "  `/knutr summarise backlog` — Deprioritised tasks for this channel\n" +
                "  `/knutr summarise backlog #channel` — Backlog for a specific channel"));
        }

        var targetChannelId = request.ChannelId;

        if (args.Length > 1)
        {
            var match = ChannelRefPattern().Match(args[1]);
            if (match.Success)
            {
                targetChannelId = match.Groups[1].Value;
            }
            else
            {
                return Task.FromResult(PluginExecuteResponse.EphemeralOk(
                    "Invalid channel reference. Use `#channel` format."));
            }
        }

        log.LogInformation("Summarise {Preset} requested for {ChannelId} by {UserId}", action, targetChannelId, request.UserId);

        _ = summaryService.GenerateAndPostAsync(targetChannelId, action, request.ChannelId, request.ThreadTs, request.UserId);

        return Task.FromResult(PluginExecuteResponse.Ok(
            $"Generating *{action}* summary for <#{targetChannelId}>... I'll post it in a thread shortly."));
    }

    public Task<PluginExecuteResponse?> ScanAsync(PluginScanRequest request, CancellationToken ct = default)
    {
        return Task.FromResult<PluginExecuteResponse?>(null);
    }

    [GeneratedRegex(@"<#(C[A-Z0-9]+)\|[^>]*>")]
    private static partial Regex ChannelRefPattern();
}
