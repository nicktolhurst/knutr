namespace Knutr.Core.PluginServices;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Core.Channels;
using Knutr.Sdk;
using Microsoft.Extensions.Logging;

/// <summary>
/// Bridges the core's command routing with remote plugin services.
/// Converts CommandContext to PluginExecuteRequest, dispatches to the remote service,
/// and converts the response back to PluginResult.
/// </summary>
public sealed class RemotePluginDispatcher(
    PluginServiceRegistry registry,
    PluginServiceClient client,
    ChannelPolicy channelPolicy,
    ILogger<RemotePluginDispatcher> logger)
{
    /// <summary>
    /// Try to dispatch a command to a remote plugin service.
    /// Returns null if no remote service handles this command.
    /// </summary>
    public async Task<PluginResult?> TryDispatchAsync(CommandContext ctx, CancellationToken ct = default)
    {
        var subcommand = ExtractSubcommand(ctx.RawText);

        // Try subcommand match first (e.g., /knutr post-mortem)
        if (subcommand is not null && registry.TryGetSubcommandService(subcommand, out var entry))
        {
            if (!channelPolicy.IsPluginEnabled(ctx.ChannelId, entry!.ServiceName))
                return null;

            return await DispatchAsync(entry, ctx, subcommand, ct);
        }

        // Try slash command match (e.g., /ping handled by remote service)
        // Normalize: Slack sends "/joke" but manifests register "joke"
        var command = ctx.Command.TrimStart('/');
        if (registry.TryGetSlashCommandService(command, out entry))
        {
            if (!channelPolicy.IsPluginEnabled(ctx.ChannelId, entry!.ServiceName))
                return null;

            return await DispatchAsync(entry, ctx, null, ct);
        }

        return null;
    }

    /// <summary>
    /// Broadcast a message to all scan-capable remote plugin services.
    /// Returns PluginResults for any that responded with content.
    /// </summary>
    public async Task<IReadOnlyList<PluginResult>> ScanAsync(MessageContext ctx, CancellationToken ct = default)
    {
        var allScanServices = registry.GetScanCapable();
        var scanServices = allScanServices
            .Where(s => channelPolicy.IsPluginEnabled(ctx.ChannelId, s.ServiceName))
            .ToList();
        if (scanServices.Count == 0)
            return [];

        var request = new PluginScanRequest
        {
            Text = ctx.Text,
            UserId = ctx.UserId,
            ChannelId = ctx.ChannelId,
            TeamId = ctx.TeamId,
            ThreadTs = ctx.ThreadTs,
            MessageTs = ctx.MessageTs,
            TraceId = ctx.CorrelationId,
        };

        var tasks = scanServices.Select(async entry =>
        {
            var response = await client.ScanAsync(entry, request, ct);
            if (response is { Success: true } && (!string.IsNullOrWhiteSpace(response.Text) || response.SuppressMention || response.Reactions is { Length: > 0 }))
            {
                logger.LogInformation("Scan hit from plugin {Service}", entry.ServiceName);
                return ToPluginResult(response, ctx.ChannelId, ctx.MessageTs);
            }
            return null;
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r is not null).ToList()!;
    }

    private async Task<PluginResult> DispatchAsync(PluginServiceEntry entry, CommandContext ctx, string? subcommand, CancellationToken ct)
    {
        var args = ExtractArgs(ctx.RawText, subcommand);

        var request = new PluginExecuteRequest
        {
            Command = ctx.Command,
            Subcommand = subcommand,
            Args = args,
            RawText = ctx.RawText,
            UserId = ctx.UserId,
            ChannelId = ctx.ChannelId,
            TeamId = ctx.TeamId,
            TraceId = ctx.CorrelationId,
        };

        var response = await client.ExecuteAsync(entry, request, ct);
        return ToPluginResult(response);
    }

    private PluginResult ToPluginResult(PluginExecuteResponse response, string? channelId = null, string? messageTs = null)
    {
        if (response.UseNaturalLanguage && response.Ephemeral)
            logger.LogWarning("Plugin response has both UseNaturalLanguage and Ephemeral set; UseNaturalLanguage takes precedence");

        PluginResult result;

        if (!response.Success)
        {
            result = PluginResult.SkipNl(new Reply(
                $"Plugin service error: {response.Error ?? "Unknown error"}",
                Markdown: false));
        }
        else if (response.UseNaturalLanguage)
        {
            result = !string.IsNullOrWhiteSpace(response.NaturalLanguageStyle)
                ? PluginResult.AskNlRewrite(response.Text ?? "", response.NaturalLanguageStyle)
                : PluginResult.AskNlFree(response.Text);
        }
        else if (response.Ephemeral)
        {
            result = PluginResult.Ephemeral(response.Text ?? "", response.Markdown);
        }
        else if (string.IsNullOrWhiteSpace(response.Text) && response.Blocks is null)
        {
            result = PluginResult.Empty();
        }
        else
        {
            result = PluginResult.SkipNl(new Reply(
                response.Text ?? "",
                Markdown: response.Markdown,
                Blocks: response.Blocks));
        }

        result.SuppressMention = response.SuppressMention;
        result.Reactions = response.Reactions;
        result.ReactToMessageTs = messageTs;
        result.ReactInChannelId = channelId;
        result.Username = response.Username;

        return result;
    }

    private static string? ExtractSubcommand(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].ToLowerInvariant() : null;
    }

    private static string[] ExtractArgs(string text, string? subcommand)
    {
        // Skip subcommand to get remaining text, then split.
        // Note: this is a simple split. Plugins that need quoted argument handling
        // should parse RawText directly, which is always included in the request.
        var remaining = text;
        if (subcommand is not null)
        {
            var idx = text.IndexOf(' ');
            remaining = idx >= 0 ? text[(idx + 1)..].TrimStart() : "";
        }
        return string.IsNullOrWhiteSpace(remaining)
            ? []
            : remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
