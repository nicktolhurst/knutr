namespace Knutr.Sdk.Testing;

/// <summary>
/// Static entry point for building test requests with sensible defaults.
/// </summary>
public static class A
{
    public static PluginExecuteRequestBuilder ExecuteRequest => new();
    public static PluginScanRequestBuilder ScanRequest => new();
}

public sealed class PluginExecuteRequestBuilder
{
    private string _command = "test";
    private string? _subcommand;
    private string[] _args = [];
    private string? _rawText;
    private string _userId = "U_TEST";
    private string _channelId = "C_TEST";
    private string? _teamId = "T_TEST";
    private string? _threadTs;
    private Dictionary<string, string>? _metadata;

    public PluginExecuteRequestBuilder WithCommand(string command) { _command = command; return this; }
    public PluginExecuteRequestBuilder WithSubcommand(string subcommand) { _subcommand = subcommand; return this; }
    public PluginExecuteRequestBuilder WithArgs(params string[] args) { _args = args; return this; }
    public PluginExecuteRequestBuilder WithRawText(string text) { _rawText = text; return this; }
    public PluginExecuteRequestBuilder WithUserId(string id) { _userId = id; return this; }
    public PluginExecuteRequestBuilder WithChannelId(string id) { _channelId = id; return this; }
    public PluginExecuteRequestBuilder WithThreadTs(string ts) { _threadTs = ts; return this; }
    public PluginExecuteRequestBuilder WithMetadata(Dictionary<string, string> metadata) { _metadata = metadata; return this; }

    public PluginExecuteRequest Build() => new()
    {
        Command = _command,
        Subcommand = _subcommand,
        Args = _args,
        RawText = _rawText,
        UserId = _userId,
        ChannelId = _channelId,
        TeamId = _teamId,
        ThreadTs = _threadTs,
        Metadata = _metadata,
    };
}

public sealed class PluginScanRequestBuilder
{
    private string _text = "";
    private string _userId = "U_TEST";
    private string _channelId = "C_TEST";
    private string? _teamId = "T_TEST";
    private string? _threadTs;
    private string? _messageTs;

    public PluginScanRequestBuilder WithText(string text) { _text = text; return this; }
    public PluginScanRequestBuilder WithUserId(string id) { _userId = id; return this; }
    public PluginScanRequestBuilder WithChannelId(string id) { _channelId = id; return this; }
    public PluginScanRequestBuilder WithThreadTs(string ts) { _threadTs = ts; return this; }
    public PluginScanRequestBuilder WithMessageTs(string ts) { _messageTs = ts; return this; }

    public PluginScanRequest Build() => new()
    {
        Text = _text,
        UserId = _userId,
        ChannelId = _channelId,
        TeamId = _teamId,
        ThreadTs = _threadTs,
        MessageTs = _messageTs,
    };
}
