using System.Text.Json;
using Knutr.Abstractions.Events;

namespace Knutr.Adapters.Slack;

public static class SlackEventTranslator
{
    public static bool TryParseMessage(JsonElement root, out MessageContext? ctx)
    {
        ctx = null;
        if (!root.TryGetProperty("event", out var ev)) return false;
        var type = ev.GetProperty("type").GetString();
        if (type != "message") return false;

        // Ignore bot messages - they have a subtype of "bot_message" or a bot_id field
        if (ev.TryGetProperty("subtype", out var subtype) && subtype.GetString() == "bot_message")
            return false;
        if (ev.TryGetProperty("bot_id", out _))
            return false;

        var team = root.TryGetProperty("team_id", out var t) ? t.GetString() ?? "" : "";
        var channel = ev.GetProperty("channel").GetString() ?? "";
        var user = ev.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "";
        var text = ev.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var thread = ev.TryGetProperty("thread_ts", out var th) ? th.GetString() : null;
        var messageTs = ev.TryGetProperty("ts", out var mts) ? mts.GetString() : null;
        var responseUrl = ev.TryGetProperty("response_url", out var ru) ? ru.GetString() : null;
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        ctx = new("slack", team, channel, user, text, thread, messageTs, CorrelationId: correlationId, ResponseUrl: responseUrl);
        return true;
    }

    public static bool TryParseCommand(JsonElement root, out CommandContext? ctx)
    {
        ctx = null;
        if (!root.TryGetProperty("command", out var cmdProp)) return false;
        var command = cmdProp.GetString() ?? "";
        var team = root.TryGetProperty("team_id", out var t) ? t.GetString() ?? "" : "";
        var channel = root.TryGetProperty("channel_id", out var c) ? c.GetString() ?? "" : "";
        var user = root.TryGetProperty("user_id", out var u) ? u.GetString() ?? "" : "";
        var text = root.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var responseUrl = root.TryGetProperty("response_url", out var r) ? r.GetString() : null;
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        ctx = new("slack", team, channel, user, command, text, responseUrl, correlationId);
        return true;
    }

    public static bool TryParseBlockAction(JsonElement root, out BlockActionContext? ctx)
    {
        ctx = null;

        // Slack sends block_actions type for button clicks
        if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "block_actions")
            return false;

        if (!root.TryGetProperty("actions", out var actions) || actions.GetArrayLength() == 0)
            return false;

        var action = actions[0];
        var actionId = action.TryGetProperty("action_id", out var aid) ? aid.GetString() ?? "" : "";
        var actionValue = action.TryGetProperty("value", out var val) ? val.GetString() : null;
        var blockId = action.TryGetProperty("block_id", out var bid) ? bid.GetString() : null;

        var team = root.TryGetProperty("team", out var teamObj) && teamObj.TryGetProperty("id", out var tid)
            ? tid.GetString() ?? "" : "";

        var channel = root.TryGetProperty("channel", out var chObj) && chObj.TryGetProperty("id", out var cid)
            ? cid.GetString() ?? "" : "";

        var user = root.TryGetProperty("user", out var userObj) && userObj.TryGetProperty("id", out var uid)
            ? uid.GetString() ?? "" : "";

        var responseUrl = root.TryGetProperty("response_url", out var rurl) ? rurl.GetString() ?? "" : "";
        var triggerId = root.TryGetProperty("trigger_id", out var trig) ? trig.GetString() ?? "" : "";

        // Get the message timestamp if available (for updating the original message)
        string? messageTs = null;
        if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("ts", out var mts))
        {
            messageTs = mts.GetString();
        }

        ctx = new("slack", team, channel, user, actionId, actionValue, blockId, responseUrl, triggerId, messageTs);
        return true;
    }
}
