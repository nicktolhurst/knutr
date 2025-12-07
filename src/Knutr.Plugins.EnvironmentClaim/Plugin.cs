namespace Knutr.Plugins.EnvironmentClaim;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.Workflows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Environment claim plugin - allows users to reserve environments for deployment.
///
/// Commands:
/// - /claim [environment] [note?]  - Claim an environment with optional note
/// - /release [environment]        - Release an environment
/// - /claimed                      - List all claimed environments
/// - /myclaims                     - List your claimed environments
/// - /nudge [environment]          - Ask owner to release (interactive workflow)
/// - /mutiny [environment]         - Force takeover (interactive workflow with confirmation)
/// - /expiry-check                 - Check stale claims (admin)
///
/// Hooks into GitLab pipeline deployments to:
/// - Validate: Check if environment is available before deployment
/// - BeforeExecute: Mark environment as "deploying"
/// - AfterExecute: Update claim status after deployment completes
/// </summary>
public sealed class Plugin : IBotPlugin
{
    public string Name => "EnvironmentClaim";

    private readonly IClaimStore _store;
    private readonly IWorkflowEngine _workflows;
    private readonly ILogger<Plugin> _log;

    public Plugin(IClaimStore store, IWorkflowEngine workflows, ILogger<Plugin> log)
    {
        _store = store;
        _workflows = workflows;
        _log = log;
    }

    public void Configure(IPluginContext context)
    {
        // Register commands
        context.Commands
            .Slash("claim", HandleClaim)
            .Slash("release", HandleRelease)
            .Slash("claimed", HandleListClaims)
            .Slash("myclaims", HandleMyClaims)
            .Slash("nudge", HandleNudge)
            .Slash("mutiny", HandleMutiny)
            .Slash("expiry-check", HandleExpiryCheck);

        // Hook into GitLab pipeline deployments
        context.Hooks
            .On(HookPoint.Validate, "knutr:deploy:*", ValidateDeployment, priority: 10)
            .On(HookPoint.BeforeExecute, "knutr:deploy:*", MarkDeploying, priority: 0)
            .On(HookPoint.AfterExecute, "knutr:deploy:*", UpdateAfterDeploy, priority: 0);
    }

    // ─────────────────────────────────────────────────────────────────
    // Command Handlers
    // ─────────────────────────────────────────────────────────────────

    private Task<PluginResult> HandleClaim(CommandContext ctx)
    {
        var parts = ctx.RawText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var env = parts.Length > 0 ? parts[0] : null;
        var note = parts.Length > 1 ? parts[1] : null;

        if (string.IsNullOrEmpty(env))
        {
            return Task.FromResult(HelpResponse());
        }

        var result = _store.TryClaim(env, ctx.UserId, note);

        if (result.Success)
        {
            var noteText = note is not null ? $"\n• *Note:* {note}" : "";
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                $":lock: Environment `{env}` claimed!{noteText}\n\n" +
                $"Others will be blocked from deploying. Use `/release {env}` when done.",
                Markdown: true)));
        }

        if (result.BlockedByUserId == ctx.UserId)
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                $":information_source: You already have `{env}` claimed.",
                Markdown: true)));
        }

        return Task.FromResult(PluginResult.SkipNl(new Reply(
            $":no_entry: Environment `{env}` is claimed by <@{result.BlockedByUserId}>.\n\n" +
            $"Use `/nudge {env}` to ask them to release or `/mutiny {env}` to force takeover.",
            Markdown: true)));
    }

    private Task<PluginResult> HandleRelease(CommandContext ctx)
    {
        var env = ctx.RawText.Trim();

        if (string.IsNullOrEmpty(env))
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                "Usage: `/release [environment]`",
                Markdown: true)));
        }

        var claim = _store.Get(env);
        if (claim is null)
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                $":information_source: Environment `{env}` is not claimed.",
                Markdown: true)));
        }

        if (claim.UserId != ctx.UserId)
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                $":no_entry: You cannot release `{env}` - it's claimed by <@{claim.UserId}>.\n\n" +
                $"Use `/mutiny {env}` to force takeover.",
                Markdown: true)));
        }

        _store.Release(env, ctx.UserId);

        return Task.FromResult(PluginResult.SkipNl(new Reply(
            $":unlock: Environment `{env}` released!",
            Markdown: true)));
    }

    private Task<PluginResult> HandleListClaims(CommandContext ctx)
    {
        var claims = _store.GetAll();

        if (claims.Count == 0)
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                ":white_check_mark: No environments are currently claimed.",
                Markdown: true)));
        }

        var lines = claims.Select(c =>
        {
            var duration = FormatDuration(c.ClaimDuration);
            var status = c.Status != ClaimStatus.Claimed ? $" [{c.Status}]" : "";
            var note = c.Note is not null ? $" - _{c.Note}_" : "";
            return $"• `{c.Environment}` - <@{c.UserId}> ({duration}){status}{note}";
        });

        var message = "*Claimed Environments*\n" + string.Join("\n", lines);

        return Task.FromResult(PluginResult.SkipNl(new Reply(message, Markdown: true)));
    }

    private Task<PluginResult> HandleMyClaims(CommandContext ctx)
    {
        var claims = _store.GetByUser(ctx.UserId);

        if (claims.Count == 0)
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                ":information_source: You don't have any environments claimed.",
                Markdown: true)));
        }

        var lines = claims.Select(c =>
        {
            var duration = FormatDuration(c.ClaimDuration);
            var note = c.Note is not null ? $" - _{c.Note}_" : "";
            return $"• `{c.Environment}` ({duration}){note}";
        });

        var message = "*Your Claimed Environments*\n" + string.Join("\n", lines) +
            "\n\nUse `/release [env]` to release.";

        return Task.FromResult(PluginResult.SkipNl(new Reply(message, Markdown: true)));
    }

    private async Task<PluginResult> HandleNudge(CommandContext ctx)
    {
        var env = ctx.RawText.Trim();

        if (string.IsNullOrEmpty(env))
        {
            return PluginResult.SkipNl(new Reply(
                "Usage: `/nudge [environment]`\n\n" +
                "Sends a message to the claim owner asking them to release.",
                Markdown: true));
        }

        var claim = _store.Get(env);
        if (claim is null)
        {
            return PluginResult.SkipNl(new Reply(
                $":information_source: Environment `{env}` is not claimed.",
                Markdown: true));
        }

        if (claim.UserId == ctx.UserId)
        {
            return PluginResult.SkipNl(new Reply(
                $":thinking_face: You own `{env}`. Did you mean `/release {env}`?",
                Markdown: true));
        }

        // Start nudge workflow
        var workflowId = await _workflows.StartAsync(
            "claim:nudge",
            ctx,
            new Dictionary<string, object>
            {
                ["environment"] = env,
                ["requester_id"] = ctx.UserId
            });

        _log.LogInformation("Started nudge workflow {WorkflowId} for {Env} by {User}",
            workflowId, env, ctx.UserId);

        return PluginResult.SkipNl(new Reply(
            $":wave: Nudging <@{claim.UserId}> about `{env}`...\n\n" +
            $"_They'll be asked if they want to release._",
            Markdown: true));
    }

    private async Task<PluginResult> HandleMutiny(CommandContext ctx)
    {
        var env = ctx.RawText.Trim();

        if (string.IsNullOrEmpty(env))
        {
            return PluginResult.SkipNl(new Reply(
                "*Mutiny - Force Environment Takeover*\n\n" +
                "Usage: `/mutiny [environment]`\n\n" +
                ":warning: Use this when you need an environment urgently and the owner is unavailable.\n" +
                "The owner will be notified of the takeover.",
                Markdown: true));
        }

        var claim = _store.Get(env);
        if (claim is null)
        {
            return PluginResult.SkipNl(new Reply(
                $":information_source: Environment `{env}` is not claimed. Use `/claim {env}` instead.",
                Markdown: true));
        }

        if (claim.UserId == ctx.UserId)
        {
            return PluginResult.SkipNl(new Reply(
                $":thinking_face: You already own `{env}`. No mutiny needed!",
                Markdown: true));
        }

        // Start mutiny workflow
        var workflowId = await _workflows.StartAsync(
            "claim:mutiny",
            ctx,
            new Dictionary<string, object>
            {
                ["environment"] = env
            });

        _log.LogInformation("Started mutiny workflow {WorkflowId} for {Env} by {User}",
            workflowId, env, ctx.UserId);

        return PluginResult.SkipNl(new Reply(
            $":pirate_flag: Initiating mutiny for `{env}`...\n\n" +
            $"_You'll be asked to confirm the takeover._",
            Markdown: true));
    }

    private async Task<PluginResult> HandleExpiryCheck(CommandContext ctx)
    {
        var parts = ctx.RawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var env = parts.Length > 0 ? parts[0] : null;

        var initialState = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(env))
        {
            initialState["environment"] = env;
        }

        var workflowId = await _workflows.StartAsync(
            "claim:expiry-check",
            ctx,
            initialState);

        _log.LogInformation("Started expiry check workflow {WorkflowId}", workflowId);

        var target = string.IsNullOrEmpty(env) ? "all stale claims" : $"`{env}`";
        return PluginResult.SkipNl(new Reply(
            $":clock3: Starting expiry check for {target}...",
            Markdown: true));
    }

    // ─────────────────────────────────────────────────────────────────
    // Hook Handlers
    // ─────────────────────────────────────────────────────────────────

    private Task<HookResult> ValidateDeployment(HookContext context, CancellationToken ct)
    {
        var env = context.Arguments.TryGetValue("environment", out var e) ? e as string : null;

        if (string.IsNullOrEmpty(env))
            return Task.FromResult(HookResult.Ok());

        if (_store.IsAvailable(env, context.UserId ?? ""))
            return Task.FromResult(HookResult.Ok());

        var claim = _store.Get(env);
        return Task.FromResult(HookResult.Reject(
            $"Environment `{env}` is claimed by <@{claim?.UserId}>.\n" +
            $"Use `/nudge {env}` to ask them or `/mutiny {env}` to force takeover."));
    }

    private Task<HookResult> MarkDeploying(HookContext context, CancellationToken ct)
    {
        var env = context.Arguments.TryGetValue("environment", out var e) ? e as string : null;

        if (!string.IsNullOrEmpty(env))
        {
            _store.UpdateStatus(env, ClaimStatus.Deploying);
        }

        return Task.FromResult(HookResult.Ok());
    }

    private Task<HookResult> UpdateAfterDeploy(HookContext context, CancellationToken ct)
    {
        var env = context.Arguments.TryGetValue("environment", out var e) ? e as string : null;

        if (!string.IsNullOrEmpty(env))
        {
            _store.UpdateStatus(env, ClaimStatus.Claimed);
            _store.RecordActivity(env);
        }

        return Task.FromResult(HookResult.Ok());
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static PluginResult HelpResponse() => PluginResult.SkipNl(new Reply(
        "*Environment Claims*\n\n" +
        "*Basic Commands:*\n" +
        "`/claim [env] [note?]` - Claim an environment\n" +
        "`/release [env]` - Release your claim\n" +
        "`/claimed` - List all claims\n" +
        "`/myclaims` - List your claims\n\n" +
        "*Interactive:*\n" +
        "`/nudge [env]` - Ask owner to release\n" +
        "`/mutiny [env]` - Force takeover\n\n" +
        "*Examples:*\n" +
        "• `/claim demo Working on feature X`\n" +
        "• `/nudge staging`\n" +
        "• `/mutiny production`",
        Markdown: true));

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{(int)duration.TotalMinutes}m";
    }
}
