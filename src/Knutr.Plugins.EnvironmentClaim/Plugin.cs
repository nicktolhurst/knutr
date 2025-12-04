namespace Knutr.Plugins.EnvironmentClaim;

using System.Collections.Concurrent;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Microsoft.Extensions.Logging;

/// <summary>
/// Environment claim plugin - allows users to reserve environments for deployment.
///
/// Commands:
/// - /claim [environment]     - Claim an environment
/// - /release [environment]   - Release an environment
/// - /claimed                 - List claimed environments
///
/// Hooks into GitLab pipeline deployments to:
/// - Validate: Check if environment is available before deployment
/// - BeforeExecute: Mark environment as "deploying"
/// - AfterExecute: Update claim status after deployment completes
/// </summary>
public sealed class Plugin : IBotPlugin
{
    public string Name => "EnvironmentClaim";

    private readonly ILogger<Plugin> _log;

    // In-memory store (replace with persistent storage in production)
    private readonly ConcurrentDictionary<string, EnvironmentClaim> _claims = new(StringComparer.OrdinalIgnoreCase);

    public Plugin(ILogger<Plugin> log)
    {
        _log = log;
    }

    public void Configure(IPluginContext context)
    {
        // Register our own commands
        context.Commands
            .Slash("claim", HandleClaim)
            .Slash("release", HandleRelease)
            .Slash("claimed", HandleListClaims);

        // Hook into GitLab pipeline deployments
        context.Hooks
            // Validate: Reject deployments to claimed environments (unless claimed by same user)
            .On(HookPoint.Validate, "knutr:deploy:*", ValidateDeployment, priority: 10)
            // BeforeExecute: Mark environment as "deploying"
            .On(HookPoint.BeforeExecute, "knutr:deploy:*", MarkDeploying, priority: 0)
            // AfterExecute: Update status after deployment
            .On(HookPoint.AfterExecute, "knutr:deploy:*", UpdateAfterDeploy, priority: 0);
    }

    private Task<PluginResult> HandleClaim(CommandContext ctx)
    {
        var env = ctx.RawText.Trim();

        if (string.IsNullOrEmpty(env))
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                "Usage: `/claim [environment]`\n\nExample: `/claim demo`",
                Markdown: true)));
        }

        if (_claims.TryGetValue(env, out var existing))
        {
            if (existing.UserId == ctx.UserId)
            {
                return Task.FromResult(PluginResult.SkipNl(new Reply(
                    $":information_source: You already have `{env}` claimed.",
                    Markdown: true)));
            }

            return Task.FromResult(PluginResult.SkipNl(new Reply(
                $":no_entry: Environment `{env}` is already claimed by <@{existing.UserId}> since {existing.ClaimedAt:g}.",
                Markdown: true)));
        }

        var claim = new EnvironmentClaim(ctx.UserId, DateTime.UtcNow);
        _claims[env] = claim;

        _log.LogInformation("User {UserId} claimed environment {Environment}", ctx.UserId, env);

        return Task.FromResult(PluginResult.SkipNl(new Reply(
            $":lock: Environment `{env}` claimed! Others will be blocked from deploying here.",
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

        if (!_claims.TryGetValue(env, out var existing))
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                $":information_source: Environment `{env}` is not claimed.",
                Markdown: true)));
        }

        if (existing.UserId != ctx.UserId)
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                $":no_entry: You cannot release `{env}` - it's claimed by <@{existing.UserId}>.",
                Markdown: true)));
        }

        _claims.TryRemove(env, out _);
        _log.LogInformation("User {UserId} released environment {Environment}", ctx.UserId, env);

        return Task.FromResult(PluginResult.SkipNl(new Reply(
            $":unlock: Environment `{env}` released!",
            Markdown: true)));
    }

    private Task<PluginResult> HandleListClaims(CommandContext ctx)
    {
        if (_claims.IsEmpty)
        {
            return Task.FromResult(PluginResult.SkipNl(new Reply(
                ":white_check_mark: No environments are currently claimed.",
                Markdown: true)));
        }

        var lines = _claims.Select(kv =>
            $"• `{kv.Key}` - <@{kv.Value.UserId}> ({kv.Value.Status}) since {kv.Value.ClaimedAt:g}");

        var message = "*Claimed Environments*\n" + string.Join("\n", lines);

        return Task.FromResult(PluginResult.SkipNl(new Reply(message, Markdown: true)));
    }

    // Hook: Validate deployment - check if environment is available
    private Task<HookResult> ValidateDeployment(HookContext context, CancellationToken ct)
    {
        var env = context.Arguments.TryGetValue("environment", out var e) ? e as string : null;

        if (string.IsNullOrEmpty(env))
        {
            return Task.FromResult(HookResult.Ok());
        }

        if (!_claims.TryGetValue(env, out var claim))
        {
            // Not claimed, allow deployment
            return Task.FromResult(HookResult.Ok());
        }

        // Check if same user
        if (claim.UserId == context.UserId)
        {
            _log.LogDebug("Deployment to {Env} allowed - user owns the claim", env);
            return Task.FromResult(HookResult.Ok());
        }

        // Different user, reject
        _log.LogInformation("Deployment to {Env} rejected - claimed by {ClaimUser}", env, claim.UserId);

        return Task.FromResult(HookResult.Reject(
            $"Environment `{env}` is claimed by <@{claim.UserId}>. Ask them to `/release {env}` first."));
    }

    // Hook: Before execute - mark environment as deploying
    private Task<HookResult> MarkDeploying(HookContext context, CancellationToken ct)
    {
        var env = context.Arguments.TryGetValue("environment", out var e) ? e as string : null;

        if (string.IsNullOrEmpty(env))
        {
            return Task.FromResult(HookResult.Ok());
        }

        if (_claims.TryGetValue(env, out var claim))
        {
            // Update status to deploying
            _claims[env] = claim with { Status = ClaimStatus.Deploying };
            _log.LogDebug("Marked {Env} as deploying", env);
        }

        return Task.FromResult(HookResult.Ok());
    }

    // Hook: After execute - update claim status
    private Task<HookResult> UpdateAfterDeploy(HookContext context, CancellationToken ct)
    {
        var env = context.Arguments.TryGetValue("environment", out var e) ? e as string : null;

        if (string.IsNullOrEmpty(env))
        {
            return Task.FromResult(HookResult.Ok());
        }

        if (_claims.TryGetValue(env, out var claim))
        {
            // Update status back to claimed with last deploy time
            _claims[env] = claim with
            {
                Status = ClaimStatus.Claimed,
                LastDeployAt = DateTime.UtcNow
            };
            _log.LogDebug("Updated {Env} claim after deployment", env);
        }

        return Task.FromResult(HookResult.Ok());
    }
}

public enum ClaimStatus { Claimed, Deploying }

public sealed record EnvironmentClaim(
    string UserId,
    DateTime ClaimedAt,
    ClaimStatus Status = ClaimStatus.Claimed,
    DateTime? LastDeployAt = null);
