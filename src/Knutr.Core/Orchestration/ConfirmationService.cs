namespace Knutr.Core.Orchestration;

using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Intent;
using Knutr.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

/// <summary>
/// Handles confirmation flows for intent-based actions.
/// Shows ephemeral confirmation messages and executes actions on approval.
/// </summary>
public sealed class ConfirmationService(
    ICommandRegistry registry,
    IHttpClientFactory httpFactory,
    ILogger<ConfirmationService> log) : IConfirmationService
{
    private readonly ConcurrentDictionary<string, PendingConfirmation> _pending = new();
    private readonly HttpClient _http = httpFactory.CreateClient("slack");

    public async Task RequestConfirmationAsync(MessageContext ctx, IntentResult intent, CancellationToken ct = default)
    {
        var confirmationId = Guid.NewGuid().ToString("N")[..12];
        var description = FormatIntentDescription(intent);

        // Store the pending confirmation
        _pending[confirmationId] = new PendingConfirmation(ctx, intent, DateTime.UtcNow);

        // Build the confirmation blocks
        var blocks = BuildConfirmationBlocks(confirmationId, intent, description);

        // Send ephemeral message (we need to use the Slack API directly for ephemeral messages to specific users)
        await PostEphemeralWithBlocksAsync(ctx.ChannelId, ctx.UserId, description, blocks, ct);

        log.LogInformation("Confirmation requested: {ConfirmationId} for intent {Action}", confirmationId, intent.Action);
    }

    public async Task HandleActionAsync(BlockActionContext ctx, CancellationToken ct = default)
    {
        // Parse the action - format is "{confirmationId}_{approve|deny}"
        var parts = ctx.ActionId.Split('_');
        if (parts.Length < 2)
        {
            log.LogWarning("Invalid action ID format: {ActionId}", ctx.ActionId);
            return;
        }

        var confirmationId = parts[0];
        var action = parts[^1]; // Last part is approve/deny

        if (!_pending.TryRemove(confirmationId, out var pending))
        {
            log.LogWarning("Confirmation not found or expired: {ConfirmationId}", confirmationId);
            await UpdateEphemeralMessageAsync(ctx.ResponseUrl, "This confirmation has expired or was already processed.", ct);
            return;
        }

        if (action == "deny")
        {
            log.LogInformation("Action denied: {ConfirmationId}", confirmationId);
            await UpdateEphemeralMessageAsync(ctx.ResponseUrl, ":x: Action cancelled.", ct);
            return;
        }

        if (action == "approve")
        {
            log.LogInformation("Action approved: {ConfirmationId}, executing {Action}", confirmationId, pending.Intent.Action);

            // Update the message to show it's being processed
            await UpdateEphemeralMessageAsync(ctx.ResponseUrl, $":hourglass: Executing {pending.Intent.Action}...", ct);

            // Create a synthetic CommandContext to execute the action
            // RawText should contain the full command arguments: "deploy main demo"
            var rawText = BuildRawTextFromIntent(pending.Intent);
            var cmdCtx = new CommandContext(
                pending.OriginalContext.Adapter,
                pending.OriginalContext.TeamId,
                pending.OriginalContext.ChannelId,
                ctx.UserId,
                "/knutr",  // The plugin registers for /knutr and parses action from RawText
                rawText,
                ctx.ResponseUrl
            );

            // Try to execute via the command registry - plugin registers for "/knutr"
            if (registry.TryMatch(cmdCtx, out var handler) && handler != null)
            {
                try
                {
                    var result = await handler(cmdCtx);
                    // Result will be sent via the normal reply flow through response_url
                    log.LogInformation("Action executed successfully: {Action}", pending.Intent.Action);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Error executing action: {Action}", pending.Intent.Action);
                    await UpdateEphemeralMessageAsync(ctx.ResponseUrl,
                        $":x: Error executing {pending.Intent.Action}: {ex.Message}", ct);
                }
            }
            else
            {
                await UpdateEphemeralMessageAsync(ctx.ResponseUrl,
                    $":warning: No handler found for command. Please use `/knutr {pending.Intent.Action}` directly.", ct);
            }
        }
    }

    private static string FormatIntentDescription(IntentResult intent)
    {
        return intent.Action?.ToLowerInvariant() switch
        {
            "deploy" => $"Deploy *{intent.Parameters.GetValueOrDefault("branch", "main")}* to *{intent.Parameters.GetValueOrDefault("env", "default")}*",
            "build" => $"Build *{intent.Parameters.GetValueOrDefault("branch", "main")}*",
            "status" => "Show pipeline status",
            "cancel" => $"Cancel pipeline {intent.Parameters.GetValueOrDefault("id", "(latest)")}",
            "retry" => $"Retry pipeline {intent.Parameters.GetValueOrDefault("id", "(latest)")}",
            _ => $"Execute {intent.Action}"
        };
    }

    private static string BuildRawTextFromIntent(IntentResult intent)
    {
        return intent.Action?.ToLowerInvariant() switch
        {
            "deploy" => $"{intent.Action} {intent.Parameters.GetValueOrDefault("branch", "main")} {intent.Parameters.GetValueOrDefault("env", "")}".Trim(),
            "build" => $"{intent.Action} {intent.Parameters.GetValueOrDefault("branch", "main")}",
            "status" => "status",
            "cancel" => $"{intent.Action} {intent.Parameters.GetValueOrDefault("id", "")}".Trim(),
            "retry" => $"{intent.Action} {intent.Parameters.GetValueOrDefault("id", "")}".Trim(),
            _ => intent.Action ?? ""
        };
    }

    private static object[] BuildConfirmationBlocks(string confirmationId, IntentResult intent, string description)
    {
        return
        [
            new Dictionary<string, object>
            {
                ["type"] = "section",
                ["text"] = new Dictionary<string, object>
                {
                    ["type"] = "mrkdwn",
                    ["text"] = $":robot_face: *Confirm Action*\n{description}"
                }
            },
            new Dictionary<string, object>
            {
                ["type"] = "actions",
                ["block_id"] = $"confirm_{confirmationId}",
                ["elements"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "button",
                        ["text"] = new Dictionary<string, object>
                        {
                            ["type"] = "plain_text",
                            ["text"] = "Approve"
                        },
                        ["style"] = "primary",
                        ["action_id"] = $"{confirmationId}_approve",
                        ["value"] = JsonSerializer.Serialize(intent)
                    },
                    new Dictionary<string, object>
                    {
                        ["type"] = "button",
                        ["text"] = new Dictionary<string, object>
                        {
                            ["type"] = "plain_text",
                            ["text"] = "Cancel"
                        },
                        ["style"] = "danger",
                        ["action_id"] = $"{confirmationId}_deny",
                        ["value"] = "cancel"
                    }
                }
            },
            new Dictionary<string, object>
            {
                ["type"] = "context",
                ["elements"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "mrkdwn",
                        ["text"] = $"Confidence: {intent.Confidence:P0}"
                    }
                }
            }
        ];
    }

    private async Task PostEphemeralWithBlocksAsync(string channel, string userId, string text, object[] blocks, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["channel"] = channel,
            ["user"] = userId,
            ["text"] = text,
            ["blocks"] = blocks
        };

        var resp = await _http.PostAsJsonAsync("chat.postEphemeral", payload, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("Failed to post ephemeral message: {Status} {Body}", resp.StatusCode, body);
        }
    }

    private async Task UpdateEphemeralMessageAsync(string responseUrl, string text, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["text"] = text,
            ["replace_original"] = true
        };

        using var client = httpFactory.CreateClient();
        var resp = await client.PostAsJsonAsync(responseUrl, payload, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("Failed to update ephemeral message: {Status} {Body}", resp.StatusCode, body);
        }
    }

    private sealed record PendingConfirmation(MessageContext OriginalContext, IntentResult Intent, DateTime CreatedAt);
}
