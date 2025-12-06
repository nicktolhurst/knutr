using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Knutr.Abstractions.Events;
using Knutr.Core.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Knutr.Adapters.Slack;

public static class SlackWebhookEndpoints
{
    public static void MapSlackEndpoints(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SlackWebhookEndpoints");
        var opts = app.Services.GetRequiredService<IOptions<SlackOptions>>().Value;
        var bus = app.Services.GetRequiredService<IEventBus>();

        app.MapPost("/slack/events", async (HttpContext http) =>
        {
            var body = await new StreamReader(http.Request.Body).ReadToEndAsync();
            if (opts.EnableSignatureValidation && !IsValidSlackSignature(http.Request.Headers, body, opts.SigningSecret))
            {
                logger.LogWarning("Invalid Slack signature"); return Results.Unauthorized();
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Slack URL verification handshake
            if (root.TryGetProperty("type", out var t) && t.GetString() == "url_verification")
            {
                var challenge = root.GetProperty("challenge").GetString() ?? "";
                return Results.Text(challenge, "text/plain");
            }

            if (SlackEventTranslator.TryParseMessage(root, out var msg) && msg is not null)
            {
                logger.LogInformation("Ingress: Slack message received (channel={Channel})", msg.ChannelId);
                bus.Publish<MessageContext>(msg);
            }

            return Results.Ok();
        });

        app.MapPost("/slack/commands", async (HttpContext http) =>
        {
            http.Request.EnableBuffering();
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen:true);
            var formBody = await reader.ReadToEndAsync();
            http.Request.Body.Position = 0;

            if (opts.EnableSignatureValidation && !IsValidSlackSignature(http.Request.Headers, formBody, opts.SigningSecret))
            {
                logger.LogWarning("Invalid Slack signature (slash)"); return Results.Unauthorized();
            }

            var form = await http.Request.ReadFormAsync();
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(form.ToDictionary(k => k.Key, v => v.Value.ToString())));
            if (SlackEventTranslator.TryParseCommand(doc.RootElement, out var cmd) && cmd is not null)
            {
                logger.LogInformation("Ingress: Slack slash command received ({Command})", cmd.Command);
                bus.Publish<CommandContext>(cmd);
            }
            // respond quickly; actual reply via response_url
            return Results.Ok();
        });

        // Handle interactive components (buttons, select menus, etc.)
        app.MapPost("/slack/interactivity", async (HttpContext http) =>
        {
            http.Request.EnableBuffering();
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
            var formBody = await reader.ReadToEndAsync();
            http.Request.Body.Position = 0;

            if (opts.EnableSignatureValidation && !IsValidSlackSignature(http.Request.Headers, formBody, opts.SigningSecret))
            {
                logger.LogWarning("Invalid Slack signature (interactivity)");
                return Results.Unauthorized();
            }

            var form = await http.Request.ReadFormAsync();
            if (!form.TryGetValue("payload", out var payloadValue))
            {
                logger.LogWarning("Missing payload in interactivity request");
                return Results.BadRequest();
            }

            using var doc = JsonDocument.Parse(payloadValue.ToString());
            var root = doc.RootElement;

            if (SlackEventTranslator.TryParseBlockAction(root, out var action) && action is not null)
            {
                logger.LogInformation("Ingress: Slack block action received (actionId={ActionId})", action.ActionId);
                bus.Publish<BlockActionContext>(action);
            }

            // Return 200 immediately - we'll update the message via response_url
            return Results.Ok();
        });
    }

    private static bool IsValidSlackSignature(IHeaderDictionary headers, string body, string? signingSecret)
    {
        if (string.IsNullOrWhiteSpace(signingSecret)) return false;
        if (!headers.TryGetValue("X-Slack-Signature", out var sig)) return false;
        if (!headers.TryGetValue("X-Slack-Request-Timestamp", out var ts)) return false;

        var basestring = $"v0:{ts}:{body}";
        var key = Encoding.UTF8.GetBytes(signingSecret);
        var hash = new HMACSHA256(key).ComputeHash(Encoding.UTF8.GetBytes(basestring));
        var hex = "v0=" + Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hex), Encoding.UTF8.GetBytes(sig.ToString()));
    }
}
