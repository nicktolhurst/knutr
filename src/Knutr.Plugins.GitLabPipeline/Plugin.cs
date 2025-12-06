namespace Knutr.Plugins.GitLabPipeline;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// GitLab Pipeline plugin for Knutr.
/// Usage: /knutr [command] [branch/ref] [environment]
/// Commands: deploy, build, status, cancel, retry
///
/// Deploy uses a workflow for long-running operations with:
/// - Build verification and triggering
/// - Environment availability checking
/// - Interactive prompts for alternatives
/// - Progress updates throughout
///
/// Hook patterns emitted by this plugin:
/// - knutr:deploy:{environment}   (e.g., knutr:deploy:demo, knutr:deploy:production)
/// - knutr:build:{ref}
/// - knutr:status:{environment}
/// - knutr:cancel:{environment}
/// - knutr:retry:{environment}
///
/// HookContext arguments available:
/// - "ref" (string): Branch or ref name
/// - "environment" (string): Target environment
/// - "action" (string): The action (deploy, status, etc.)
/// - "pipeline_id" (int): Pipeline ID for cancel/retry actions
/// </summary>
public sealed class Plugin : IBotPlugin
{
    public string Name => "GitLabPipeline";

    /// <summary>Well-known context keys for hook integration.</summary>
    public static class ContextKeys
    {
        public const string Ref = "ref";
        public const string Branch = "branch";
        public const string Environment = "environment";
        public const string PipelineId = "pipeline_id";
        public const string Action = "action";
        public const string Project = "project";
        public const string PipelineResult = "pipeline_result";
    }

    private readonly IGitLabClient _client;
    private readonly GitLabOptions _options;
    private readonly ILogger<Plugin> _log;
    private readonly IHookRegistry _hooks;
    private readonly IWorkflowEngine _workflowEngine;

    public Plugin(
        IGitLabClient client,
        IOptions<GitLabOptions> options,
        ILogger<Plugin> log,
        IHookRegistry hooks,
        IWorkflowEngine workflowEngine)
    {
        _client = client;
        _options = options.Value;
        _log = log;
        _hooks = hooks;
        _workflowEngine = workflowEngine;
    }

    public void Configure(IPluginContext context)
    {
        context.Commands.Slash("knutr", HandleCommand);
    }

    private async Task<PluginResult> HandleCommand(CommandContext ctx)
    {
        var args = ParseArguments(ctx.RawText);

        if (args.Command is null)
        {
            return HelpResponse();
        }

        var action = args.Command.ToLowerInvariant();

        // For help, no hooks needed
        if (action == "help")
        {
            return HelpResponse();
        }

        // Build hook context with parsed arguments
        var hookContext = new HookContext
        {
            PluginName = Name,
            Command = "knutr",
            Action = action,
            Arguments = new Dictionary<string, object?>
            {
                [ContextKeys.Action] = action,
                [ContextKeys.Ref] = args.Ref,
                [ContextKeys.Environment] = args.Environment
            },
            CommandContext = ctx
        };

        // Execute validation hooks - other plugins can reject here
        var validateResult = await _hooks.ExecuteAsync(HookPoint.Validate, hookContext);
        if (!validateResult.Continue)
        {
            return validateResult.Response
                ?? ErrorResponse(validateResult.ErrorMessage ?? "Validation failed");
        }

        // Execute before hooks - other plugins can prepare state
        var beforeResult = await _hooks.ExecuteAsync(HookPoint.BeforeExecute, hookContext);
        if (!beforeResult.Continue)
        {
            return beforeResult.Response
                ?? ErrorResponse(beforeResult.ErrorMessage ?? "Pre-execution check failed");
        }

        // Execute main action
        PluginResult result;
        try
        {
            result = action switch
            {
                "deploy" => await HandleDeployWorkflow(args, ctx),
                "build" => await HandleBuild(args, hookContext),
                "status" => await HandleStatus(args),
                "cancel" => await HandleCancel(args),
                "retry" => await HandleRetry(args),
                _ => UnknownCommandResponse(args.Command)
            };

            // Store result in context for after hooks
            hookContext.Result = result;
        }
        catch (Exception ex)
        {
            hookContext.Error = ex;
            await _hooks.ExecuteAsync(HookPoint.OnError, hookContext);
            throw;
        }

        // Execute after hooks - other plugins can react to completion
        var afterResult = await _hooks.ExecuteAsync(HookPoint.AfterExecute, hookContext);
        if (!afterResult.Continue && afterResult.Response is not null)
        {
            return afterResult.Response;
        }

        return result;
    }

    /// <summary>
    /// Starts the deploy workflow for long-running, interactive deployments.
    /// The workflow will post its own initial message to create a thread.
    /// </summary>
    private async Task<PluginResult> HandleDeployWorkflow(ParsedArgs args, CommandContext ctx)
    {
        if (string.IsNullOrEmpty(args.Ref))
        {
            return ErrorResponse("Missing branch/ref. Usage: `/knutr deploy [branch/ref] [environment]`");
        }

        if (string.IsNullOrEmpty(args.Environment))
        {
            return ErrorResponse("Missing environment. Usage: `/knutr deploy [branch/ref] [environment]`");
        }

        var initialState = new Dictionary<string, object>
        {
            [ContextKeys.Branch] = args.Ref,
            [ContextKeys.Ref] = args.Ref,
            [ContextKeys.Environment] = args.Environment
        };

        _log.LogInformation("Starting deploy workflow for {Ref} to {Environment}", args.Ref, args.Environment);

        // Start workflow - it will post its own initial message to create a thread
        await _workflowEngine.StartAsync(
            "gitlab:deploy",
            ctx,
            initialState);

        // Return empty result - the workflow handles all messaging
        return PluginResult.SkipNl(new Reply("", Markdown: false));
    }

    /// <summary>
    /// Trigger a build for a branch (simple, non-workflow action).
    /// </summary>
    private async Task<PluginResult> HandleBuild(ParsedArgs args, HookContext hookContext)
    {
        if (string.IsNullOrEmpty(args.Ref))
        {
            return ErrorResponse("Missing branch/ref. Usage: `/knutr build [branch/ref] [environment?]`");
        }

        var (project, variables) = ResolveEnvironment(args.Environment);
        project ??= _options.DefaultProject;

        if (string.IsNullOrEmpty(project))
        {
            return ErrorResponse("No project configured. Set a default project or specify an environment.");
        }

        hookContext.Set(ContextKeys.Project, project);

        _log.LogInformation("Building {Ref} (project: {Project})", args.Ref, project);

        var result = await _client.TriggerPipelineAsync(project, args.Ref, variables);

        if (!result.IsSuccess)
        {
            return ErrorResponse($"Failed to trigger build: {result.ErrorMessage}");
        }

        var pipeline = result.Pipeline!;
        hookContext.Set(ContextKeys.PipelineResult, pipeline);

        return PluginResult.SkipNl(new Reply(
            $":construction: *Build triggered!*\n" +
            $"• *Branch:* `{args.Ref}`\n" +
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
        "*Workflows (interactive, long-running):*\n" +
        "`/knutr deploy [branch/ref] [environment]` - Full deployment workflow with build verification\n\n" +
        "*Quick Actions:*\n" +
        "`/knutr build [branch/ref] [environment?]` - Trigger a build pipeline\n" +
        "`/knutr status [branch/ref] [environment?]` - Get latest pipeline status\n" +
        "`/knutr cancel [pipeline_id] [environment?]` - Cancel a running pipeline\n" +
        "`/knutr retry [pipeline_id] [environment?]` - Retry a failed pipeline\n" +
        "`/knutr help` - Show this help message\n\n" +
        "*Examples:*\n" +
        "• `/knutr deploy feature/new-feature demo` - _Starts interactive deployment_\n" +
        "• `/knutr build main` - _Quick build trigger_\n" +
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

internal sealed record ParsedArgs
{
    public string? Command { get; init; }
    public string? Ref { get; init; }
    public string? Environment { get; init; }
}
