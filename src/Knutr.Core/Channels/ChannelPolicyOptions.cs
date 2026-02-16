namespace Knutr.Core.Channels;

public sealed class ChannelPolicyOptions
{
    public const string SectionName = "Channels";
    public bool AllowAll { get; set; } = false;
    public Dictionary<string, ChannelConfig> Allowlist { get; set; } = new();
}

public sealed class ChannelConfig
{
    public string? Name { get; set; }
    public List<string> Plugins { get; set; } = [];
}
