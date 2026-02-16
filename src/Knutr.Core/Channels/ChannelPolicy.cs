namespace Knutr.Core.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class ChannelPolicy(
    IOptionsMonitor<ChannelPolicyOptions> options,
    ILogger<ChannelPolicy> logger)
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    public bool IsChannelAllowed(string channelId)
    {
        if (options.CurrentValue.AllowAll)
            return true;

        // DMs always bypass the allowlist
        if (channelId.StartsWith('D'))
            return true;

        if (options.CurrentValue.Allowlist.ContainsKey(channelId))
            return true;

        logger.LogWarning("Channel {ChannelId} is not in the allowlist — dropping event", channelId);
        return false;
    }

    public bool IsPluginEnabled(string channelId, string serviceName)
    {
        if (options.CurrentValue.AllowAll)
            return true;

        // DMs bypass plugin filtering
        if (channelId.StartsWith('D'))
            return true;

        if (!options.CurrentValue.Allowlist.TryGetValue(channelId, out var config))
            return false;

        return config.Plugins.Contains(serviceName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> GetEnabledPlugins(string channelId)
    {
        if (options.CurrentValue.AllowAll)
            return EmptySet; // caller should treat empty as "all allowed"

        if (channelId.StartsWith('D'))
            return EmptySet;

        if (!options.CurrentValue.Allowlist.TryGetValue(channelId, out var config))
            return EmptySet;

        return new HashSet<string>(config.Plugins, StringComparer.OrdinalIgnoreCase);
    }
}
