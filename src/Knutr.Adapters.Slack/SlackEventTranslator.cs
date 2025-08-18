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
        var team = root.TryGetProperty("team_id", out var t) ? t.GetString() ?? "" : "";
        var channel = ev.GetProperty("channel").GetString() ?? "";
        var user = ev.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "";
        var text = ev.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        var thread = ev.TryGetProperty("thread_ts", out var th) ? th.GetString() : null;
        ctx = new("slack", team, channel, user, text, thread);
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
        ctx = new("slack", team, channel, user, command, text, responseUrl);
        return true;
    }
}
