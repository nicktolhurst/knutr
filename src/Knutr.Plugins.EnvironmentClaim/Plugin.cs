namespace Knutr.Plugins.EnvironmentClaim;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Messaging;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.Workflows;
using Messaging;
using Microsoft.Extensions.Logging;

/// <summary>
/// Environment claim plugin - allows users to reserve environments for deployment.
///
/// Commands (via /knutr subcommands):
/// - /knutr claim [environment] [note?]  - Claim an environment with optional note
/// - /knutr release [environment]        - Release an environment
/// - /knutr claimed                      - List all claimed environments
/// - /knutr myclaims                     - List your claimed environments
/// - /knutr nudge [environment]          - Ask owner to release (interactive workflow)
/// - /knutr mutiny [environment]         - Force takeover (interactive workflow with confirmation)
///
/// All responses are ephemeral (only visible to the user who ran the command).
/// Claim confirmations are sent via DM for a permanent record.
///
/// Hooks into deployments to validate environment availability.
/// </summary>
public sealed class Plugin : IBotPlugin
{
    public string Name => "EnvironmentClaim";

    private readonly IClaimStore _store;
    private readonly IWorkflowEngine _workflows;
    private readonly IMessagingService _messaging;
    private readonly ILogger<Plugin> _log;

    public Plugin(
        IClaimStore store,
        IWorkflowEngine workflows,
        IMessagingService messaging,
        ILogger<Plugin> log)
    {
        _store = store;
        _workflows = workflows;
        _messaging = messaging;
        _log = log;
    }

    public void Configure(IPluginContext context)
    {
        // Register subcommands under /knutr
        context.Subcommands
            .Subcommand("claim", HandleClaim)
            .Subcommand("release", HandleRelease)
            .Subcommand("claimed", HandleListClaims)
            .Subcommand("myclaims", HandleMyClaims)
            .Subcommand("nudge", HandleNudge)
            .Subcommand("mutiny", HandleMutiny);

        // Hook into deployments to validate environment availability
        context.Hooks
            .On(HookPoint.Validate, "knutr:deploy:*", ValidateDeployment, priority: 10)
            .On(HookPoint.BeforeExecute, "knutr:deploy:*", MarkDeploying, priority: 0)
            .On(HookPoint.AfterExecute, "knutr:deploy:*", UpdateAfterDeploy, priority: 0);
    }

    // ─────────────────────────────────────────────────────────────────
    // Subcommand Handlers
    // ─────────────────────────────────────────────────────────────────

    private async Task<PluginResult> HandleClaim(CommandContext ctx, string[] args)
    {
        var environment = args.Length > 0 ? args[0] : null;
        var note = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

        if (string.IsNullOrEmpty(environment))
        {
            var (helpText, helpBlocks) = ClaimBlocks.ClaimHelp();
            return PluginResult.Ephemeral(helpText, helpBlocks);
        }

        var result = _store.TryClaim(environment, ctx.UserId, note);

        if (result.Success)
        {
            var (text, blocks) = ClaimBlocks.ClaimSuccess(environment, note);

            // Send DM confirmation
            _ = _messaging.PostDmAsync(ctx.UserId, text, blocks);

            // Ephemeral response
            return PluginResult.Ephemeral(text, blocks);
        }

        if (result.BlockedByUserId == ctx.UserId)
        {
            // User already owns this environment
            return PluginResult.Ephemeral(
                $":information_source: Looks like you already have `{environment}`!");
        }

        var (blockedText, blockedBlocks) = ClaimBlocks.ClaimBlocked(environment, result.BlockedByUserId!);
        return PluginResult.Ephemeral(blockedText, blockedBlocks);
    }

    private Task<PluginResult> HandleRelease(CommandContext ctx, string[] args)
    {
        var environment = args.Length > 0 ? args[0] : null;

        if (string.IsNullOrEmpty(environment))
        {
            var (text, blocks) = ClaimBlocks.UsageError("/knutr release [environment]", "Release your claim on an environment");
            return Task.FromResult(PluginResult.Ephemeral(text, blocks));
        }

        var claim = _store.Get(environment);
        if (claim is null)
        {
            var (text, blocks) = ClaimBlocks.ReleaseNotClaimed(environment);
            return Task.FromResult(PluginResult.Ephemeral(text, blocks));
        }

        if (claim.UserId != ctx.UserId)
        {
            var (text, blocks) = ClaimBlocks.ReleaseNotOwner(environment, claim.UserId);
            return Task.FromResult(PluginResult.Ephemeral(text, blocks));
        }

        _store.Release(environment, ctx.UserId);

        var (successText, successBlocks) = ClaimBlocks.ReleaseSuccess(environment);
        return Task.FromResult(PluginResult.Ephemeral(successText, successBlocks));
    }

    private Task<PluginResult> HandleListClaims(CommandContext ctx, string[] args)
    {
        var claims = _store.GetAll();
        var (text, blocks) = ClaimBlocks.ClaimsList(claims);
        return Task.FromResult(PluginResult.Ephemeral(text, blocks));
    }

    private Task<PluginResult> HandleMyClaims(CommandContext ctx, string[] args)
    {
        var claims = _store.GetByUser(ctx.UserId);
        var (text, blocks) = ClaimBlocks.MyClaimsList(claims);
        return Task.FromResult(PluginResult.Ephemeral(text, blocks));
    }

    private async Task<PluginResult> HandleNudge(CommandContext ctx, string[] args)
    {
        var environment = args.Length > 0 ? args[0] : null;

        if (string.IsNullOrEmpty(environment))
        {
            var (text, blocks) = ClaimBlocks.UsageError("/knutr nudge [environment]", "Ask the claim owner to release");
            return PluginResult.Ephemeral(text, blocks);
        }

        var claim = _store.Get(environment);
        if (claim is null)
        {
            var (text, blocks) = ClaimBlocks.NudgeNotClaimed(environment);
            return PluginResult.Ephemeral(text, blocks);
        }

        if (claim.UserId == ctx.UserId)
        {
            var (text, blocks) = ClaimBlocks.NudgeOwnClaim(environment);
            return PluginResult.Ephemeral(text, blocks);
        }

        var workflowId = await _workflows.StartAsync(
            "claim:nudge",
            ctx,
            new Dictionary<string, object>
            {
                ["environment"] = environment,
                ["requester_id"] = ctx.UserId
            });

        _log.LogInformation("Started nudge workflow {WorkflowId} for {Env} by {User}",
            workflowId, environment, ctx.UserId);

        // Return empty result - the workflow sends the ephemeral message
        return PluginResult.Ephemeral("");
    }

    private async Task<PluginResult> HandleMutiny(CommandContext ctx, string[] args)
    {
        var environment = args.Length > 0 ? args[0] : null;

        if (string.IsNullOrEmpty(environment))
        {
            var (text, blocks) = ClaimBlocks.UsageError("/knutr mutiny [environment]", "Force takeover of an environment");
            return PluginResult.Ephemeral(text, blocks);
        }

        var claim = _store.Get(environment);
        if (claim is null)
        {
            var (text, blocks) = ClaimBlocks.MutinyNotClaimed(environment);
            return PluginResult.Ephemeral(text, blocks);
        }

        if (claim.UserId == ctx.UserId)
        {
            var (text, blocks) = ClaimBlocks.MutinyOwnClaim(environment);
            return PluginResult.Ephemeral(text, blocks);
        }

        var workflowId = await _workflows.StartAsync(
            "claim:mutiny",
            ctx,
            new Dictionary<string, object>
            {
                ["environment"] = environment
            });

        _log.LogInformation("Started mutiny workflow {WorkflowId} for {Env} by {User}",
            workflowId, environment, ctx.UserId);

        // Return empty result - the workflow sends the ephemeral message
        return PluginResult.Ephemeral("");
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
            $"Use `/knutr nudge {env}` to ask them or `/knutr mutiny {env}` to force takeover."));
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
}
