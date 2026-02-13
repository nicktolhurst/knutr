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

    private static PluginResult ToPluginResult(PluginExecuteResponse response)
    {
        if (!response.Success)
        {
            return PluginResult.SkipNl(new Reply(
                $"Plugin service error: {response.Error ?? "Unknown error"}",
                Markdown: false));
        }

        if (response.UseNaturalLanguage)
        {
            return PluginResult.AskNlFree(response.Text);
        }

        if (response.Ephemeral)
        {
            return PluginResult.Ephemeral(response.Text ?? "", response.Markdown);
        }

        return PluginResult.SkipNl(new Reply(
            response.Text ?? "",
            Markdown: response.Markdown,
            Blocks: response.Blocks));
    }

    private static string? ExtractSubcommand(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1].ToLowerInvariant() : null;
    }

    private static string[] ExtractArgs(string text, string? subcommand)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Skip command + subcommand to get args
        var skip = subcommand is not null ? 2 : 1;
        return parts.Length > skip ? parts[skip..] : [];
    }
}
