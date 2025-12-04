namespace Knutr.Plugins.GitLabPipeline;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// GitLab Pipeline plugin for Knutr.
/// Usage: /knutr [command] [branch/ref] [environment]
/// Commands: deploy, status, cancel, retry
/// </summary>
public sealed class Plugin : IBotPlugin
{
    public string Name => "GitLabPipeline";

    private readonly IGitLabClient _client;
    private readonly GitLabOptions _options;
    private readonly ILogger<Plugin> _log;

    public Plugin(IGitLabClient client, IOptions<GitLabOptions> options, ILogger<Plugin> log)
    {
        _client = client;
        _options = options.Value;
        _log = log;
    }

    public void Configure(ICommandBuilder commands)
    {
        commands.Slash("knutr", HandleCommand);
    }

    private async Task<PluginResult> HandleCommand(CommandContext ctx)
    {
        var args = ParseArguments(ctx.RawText);

        if (args.Command is null)
        {
            return HelpResponse();
        }

        return args.Command.ToLowerInvariant() switch
        {
            "deploy" => await HandleDeploy(args),
            "status" => await HandleStatus(args),
            "cancel" => await HandleCancel(args),
            "retry" => await HandleRetry(args),
            "help" => HelpResponse(),
            _ => UnknownCommandResponse(args.Command)
        };
    }

    private async Task<PluginResult> HandleDeploy(ParsedArgs args)
    {
        if (string.IsNullOrEmpty(args.Ref))
        {
            return ErrorResponse("Missing branch/ref. Usage: `/knutr deploy [branch/ref] [environment]`");
        }

        if (string.IsNullOrEmpty(args.Environment))
        {
            return ErrorResponse("Missing environment. Usage: `/knutr deploy [branch/ref] [environment]`");
        }

        var (project, variables) = ResolveEnvironment(args.Environment);

        if (project is null)
        {
            return ErrorResponse($"Unknown environment: `{args.Environment}`. Check your GitLab configuration.");
        }

        _log.LogInformation("Deploying {Ref} to {Environment} (project: {Project})", args.Ref, args.Environment, project);

        var result = await _client.TriggerPipelineAsync(project, args.Ref, variables);

        if (!result.IsSuccess)
        {
            return ErrorResponse($"Failed to trigger pipeline: {result.ErrorMessage}");
        }

        var pipeline = result.Pipeline!;
        return PluginResult.SkipNl(new Reply(
            $":rocket: *Pipeline triggered!*\n" +
            $"• *Branch:* `{args.Ref}`\n" +
            $"• *Environment:* `{args.Environment}`\n" +
            $"• *Pipeline ID:* `#{pipeline.Id}`\n" +
            $"• *Status:* `{pipeline.Status}`\n" +
            $"• *URL:* {pipeline.WebUrl}",
            Markdown: true));
    }

    private async Task<PluginResult> HandleStatus(ParsedArgs args)
    {
        if (string.IsNullOrEmpty(args.Ref))
        {
            return ErrorResponse("Missing branch/ref. Usage: `/knutr status [branch/ref] [environment?]`");
        }

        var (project, _) = ResolveEnvironment(args.Environment);
        project ??= _options.DefaultProject;

        if (string.IsNullOrEmpty(project))
        {
            return ErrorResponse("No project configured. Set a default project or specify an environment.");
        }

        var pipeline = await _client.GetLatestPipelineAsync(project, args.Ref);

        if (pipeline is null)
        {
            return PluginResult.SkipNl(new Reply(
                $":mag: No pipelines found for `{args.Ref}`",
                Markdown: true));
        }

        var statusEmoji = GetStatusEmoji(pipeline.Status);
        return PluginResult.SkipNl(new Reply(
            $"{statusEmoji} *Latest pipeline for `{args.Ref}`*\n" +
            $"• *Pipeline ID:* `#{pipeline.Id}`\n" +
            $"• *Status:* `{pipeline.Status}`\n" +
            $"• *SHA:* `{pipeline.Sha[..8]}`\n" +
            $"• *Created:* {pipeline.CreatedAt:u}\n" +
            $"• *URL:* {pipeline.WebUrl}",
            Markdown: true));
    }

    private async Task<PluginResult> HandleCancel(ParsedArgs args)
    {
        if (string.IsNullOrEmpty(args.Ref) || !int.TryParse(args.Ref, out var pipelineId))
        {
            return ErrorResponse("Missing or invalid pipeline ID. Usage: `/knutr cancel [pipeline_id] [environment?]`");
        }

        var (project, _) = ResolveEnvironment(args.Environment);
        project ??= _options.DefaultProject;

        if (string.IsNullOrEmpty(project))
        {
            return ErrorResponse("No project configured. Set a default project or specify an environment.");
        }

        var success = await _client.CancelPipelineAsync(project, pipelineId);

        return success
            ? PluginResult.SkipNl(new Reply($":stop_sign: Pipeline `#{pipelineId}` cancelled.", Markdown: true))
            : ErrorResponse($"Failed to cancel pipeline `#{pipelineId}`. It may already be complete or cancelled.");
    }

    private async Task<PluginResult> HandleRetry(ParsedArgs args)
    {
        if (string.IsNullOrEmpty(args.Ref) || !int.TryParse(args.Ref, out var pipelineId))
        {
            return ErrorResponse("Missing or invalid pipeline ID. Usage: `/knutr retry [pipeline_id] [environment?]`");
        }

        var (project, _) = ResolveEnvironment(args.Environment);
        project ??= _options.DefaultProject;

        if (string.IsNullOrEmpty(project))
        {
            return ErrorResponse("No project configured. Set a default project or specify an environment.");
        }

        var success = await _client.RetryPipelineAsync(project, pipelineId);

        return success
            ? PluginResult.SkipNl(new Reply($":arrows_counterclockwise: Pipeline `#{pipelineId}` retry started.", Markdown: true))
            : ErrorResponse($"Failed to retry pipeline `#{pipelineId}`.");
    }

    private (string? Project, Dictionary<string, string>? Variables) ResolveEnvironment(string? environment)
    {
        if (string.IsNullOrEmpty(environment))
        {
            return (_options.DefaultProject, null);
        }

        if (_options.Environments.TryGetValue(environment, out var config))
        {
            var project = config.Project ?? _options.DefaultProject;
            var variables = config.Variables.Count > 0 ? config.Variables : null;
            return (project, variables);
        }

        // Try case-insensitive match
        var match = _options.Environments
            .FirstOrDefault(kv => kv.Key.Equals(environment, StringComparison.OrdinalIgnoreCase));

        if (match.Value is not null)
        {
            var project = match.Value.Project ?? _options.DefaultProject;
            var variables = match.Value.Variables.Count > 0 ? match.Value.Variables : null;
            return (project, variables);
        }

        return (null, null);
    }

    private static ParsedArgs ParseArguments(string rawText)
    {
        // RawText typically contains everything after the command name
        // e.g., "deploy feature/new-feature demo"
        var parts = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new ParsedArgs
        {
            Command = parts.Length > 0 ? parts[0] : null,
            Ref = parts.Length > 1 ? parts[1] : null,
            Environment = parts.Length > 2 ? parts[2] : null
        };
    }

    private static string GetStatusEmoji(string status) => status.ToLowerInvariant() switch
    {
        "success" or "passed" => ":white_check_mark:",
        "failed" => ":x:",
        "running" => ":arrows_counterclockwise:",
        "pending" => ":clock3:",
        "canceled" or "cancelled" => ":stop_sign:",
        "skipped" => ":fast_forward:",
        "manual" => ":hand:",
        "scheduled" => ":calendar:",
        _ => ":grey_question:"
    };

    private static PluginResult HelpResponse() => PluginResult.SkipNl(new Reply(
        "*GitLab Pipeline Commands*\n\n" +
        "`/knutr deploy [branch/ref] [environment]` - Trigger a deployment pipeline\n" +
        "`/knutr status [branch/ref] [environment?]` - Get latest pipeline status\n" +
        "`/knutr cancel [pipeline_id] [environment?]` - Cancel a running pipeline\n" +
        "`/knutr retry [pipeline_id] [environment?]` - Retry a failed pipeline\n" +
        "`/knutr help` - Show this help message\n\n" +
        "*Examples:*\n" +
        "• `/knutr deploy feature/new-feature demo`\n" +
        "• `/knutr deploy main production`\n" +
        "• `/knutr status feature/new-feature`\n" +
        "• `/knutr cancel 12345`",
        Markdown: true));

    private static PluginResult UnknownCommandResponse(string command) => PluginResult.SkipNl(new Reply(
        $":warning: Unknown command: `{command}`\n\nUse `/knutr help` for available commands.",
        Markdown: true));

    private static PluginResult ErrorResponse(string message) => PluginResult.SkipNl(new Reply(
        $":x: {message}",
        Markdown: true));
}

file sealed record ParsedArgs
{
    public string? Command { get; init; }
    public string? Ref { get; init; }
    public string? Environment { get; init; }
}
