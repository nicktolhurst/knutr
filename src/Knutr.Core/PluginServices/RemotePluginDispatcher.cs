namespace Knutr.Core.PluginServices;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
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
            logger.LogInformation("Routing subcommand {Subcommand} to remote service {Service}", subcommand, entry!.ServiceName);
            return await DispatchAsync(entry, ctx, subcommand, ct);
        }

        // Try slash command match (e.g., /ping handled by remote service)
        // Normalize: Slack sends "/joke" but manifests register "joke"
        var command = ctx.Command.TrimStart('/');
        if (registry.TryGetSlashCommandService(command, out entry))
        {
            logger.LogInformation("Routing slash command /{Command} to remote service {Service}", ctx.Command, entry!.ServiceName);
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
        var scanServices = registry.GetScanCapable();
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
        };

        var tasks = scanServices.Select(async entry =>
        {
            var response = await client.ScanAsync(entry, request, ct);
            if (response is { Success: true } && (response.Text is { Length: > 0 } || response.SuppressMention || response.Reactions is { Length: > 0 }))
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
        };

        var response = await client.ExecuteAsync(entry, request, ct);
        return ToPluginResult(response);
    }

    private static PluginResult ToPluginResult(PluginExecuteResponse response, string? channelId = null, string? messageTs = null)
    {
        PluginResult result;

        if (!response.Success)
        {
            result = PluginResult.SkipNl(new Reply(
                $"Plugin service error: {response.Error ?? "Unknown error"}",
                Markdown: false));
        }
        else if (response.UseNaturalLanguage)
        {
            result = response.NaturalLanguageStyle is { Length: > 0 }
                ? PluginResult.AskNlRewrite(response.Text ?? "", response.NaturalLanguageStyle)
                : PluginResult.AskNlFree(response.Text);
        }
        else if (response.Ephemeral)
        {
            result = PluginResult.Ephemeral(response.Text ?? "", response.Markdown);
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

        return result;
    }

    private static string? ExtractSubcommand(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].ToLowerInvariant() : null;
    }

    private static string[] ExtractArgs(string text, string? subcommand)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Skip subcommand to get remaining args
        var skip = subcommand is not null ? 1 : 0;
        return parts.Length > skip ? parts[skip..] : [];
    }
}
