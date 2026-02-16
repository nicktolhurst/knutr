using System.Net;
using System.Text.Json;
using FluentAssertions;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Core.Channels;
using Knutr.Core.PluginServices;
using Knutr.Sdk;
using Knutr.Sdk.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Knutr.Tests.Core;

public class RemotePluginDispatcherTests
{
    private readonly PluginServiceRegistry _registry = new(NullLogger<PluginServiceRegistry>.Instance);
    private readonly RemotePluginDispatcher _dispatcher;
    private readonly FakeHttpMessageHandler _httpHandler;

    public RemotePluginDispatcherTests()
    {
        _httpHandler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = true, Text = "ok" }),
                    System.Text.Encoding.UTF8, "application/json")
            });

        var httpFactory = new FakeHttpClientFactory(_httpHandler);
        var pluginOptions = Options.Create(new PluginServiceOptions());
        var client = new PluginServiceClient(httpFactory, pluginOptions, NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var channelPolicy = new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance);

        _dispatcher = new RemotePluginDispatcher(_registry, client, channelPolicy, NullLogger<RemotePluginDispatcher>.Instance);
    }

    private static PluginServiceEntry MakeEntry(
        string name,
        string[]? subcommands = null,
        string[]? slashCommands = null,
        bool supportsScan = false)
    {
        return new PluginServiceEntry
        {
            ServiceName = name,
            BaseUrl = $"http://{name}:8080",
            Manifest = new PluginManifest
            {
                Name = name,
                Version = "1.0.0",
                Subcommands = (subcommands ?? []).Select(s => new PluginSubcommand { Name = s }).ToList(),
                SlashCommands = (slashCommands ?? []).Select(c => new PluginSlashCommand { Command = c }).ToList(),
                SupportsScan = supportsScan,
            }
        };
    }

    private static CommandContext CmdCtx(string command, string rawText)
        => new("slack", "T1", "C_TEST", "U_USER", command, rawText);

    private static MessageContext MsgCtx(string text, string? messageTs = null)
        => new("slack", "T1", "C_TEST", "U_USER", text, MessageTs: messageTs);

    // ── TryDispatchAsync ──

    [Fact]
    public async Task TryDispatch_SubcommandMatch_ReturnsResult()
    {
        _registry.Register(MakeEntry("sentinel", subcommands: ["sentinel"]));

        var result = await _dispatcher.TryDispatchAsync(CmdCtx("knutr", "sentinel status"));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task TryDispatch_SlashCommandMatch_ReturnsResult()
    {
        _registry.Register(MakeEntry("joke", slashCommands: ["joke"]));

        var result = await _dispatcher.TryDispatchAsync(CmdCtx("/joke", ""));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task TryDispatch_NoMatch_ReturnsNull()
    {
        var result = await _dispatcher.TryDispatchAsync(CmdCtx("knutr", "nonexistent"));
        result.Should().BeNull();
    }

    [Fact]
    public async Task TryDispatch_ChannelPolicyDisabled_ReturnsNull()
    {
        _registry.Register(MakeEntry("sentinel", subcommands: ["sentinel"]));

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions
        {
            AllowAll = false,
            Allowlist = new Dictionary<string, ChannelConfig>
            {
                ["C_TEST"] = new() { Plugins = ["joke"] } // sentinel not listed
            }
        });
        var restrictedPolicy = new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance);

        var httpFactory = new FakeHttpClientFactory(_httpHandler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);
        var dispatcher = new RemotePluginDispatcher(_registry, client, restrictedPolicy, NullLogger<RemotePluginDispatcher>.Instance);

        var result = await dispatcher.TryDispatchAsync(CmdCtx("knutr", "sentinel status"));
        result.Should().BeNull();
    }

    // ── ScanAsync ──

    [Fact]
    public async Task Scan_NoScanServices_ReturnsEmpty()
    {
        var results = await _dispatcher.ScanAsync(MsgCtx("hello"));
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_WithScanHit_ReturnsResult()
    {
        _registry.Register(MakeEntry("jargonbuster", supportsScan: true));

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = true, Text = "SRE: Site Reliability Engineering", Reactions = new[] { "knutr-teach-me" } }),
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        var results = await dispatcher.ScanAsync(MsgCtx("We need SRE practices", messageTs: "123"));
        results.Should().ContainSingle();
    }

    [Fact]
    public async Task Scan_NullResponse_Filtered()
    {
        _registry.Register(MakeEntry("scanner", supportsScan: true));

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        var results = await dispatcher.ScanAsync(MsgCtx("hello"));
        results.Should().BeEmpty();
    }

    // ── ToPluginResult (tested indirectly via TryDispatch) ──

    [Fact]
    public async Task TryDispatch_FailedResponse_ReturnsErrorText()
    {
        _registry.Register(MakeEntry("failsvc", slashCommands: ["fail"]));

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = false, Error = "something broke" }),
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        var result = await dispatcher.TryDispatchAsync(CmdCtx("/fail", ""));
        result.Should().NotBeNull();
        result!.PassThrough.Should().NotBeNull();
        result.PassThrough!.Reply.Text.Should().Contain("something broke");
    }

    [Fact]
    public async Task TryDispatch_NlResponse_ReturnsAskNl()
    {
        _registry.Register(MakeEntry("nlsvc", slashCommands: ["nl"]));

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = true, Text = "raw data", UseNaturalLanguage = true }),
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        var result = await dispatcher.TryDispatchAsync(CmdCtx("/nl", ""));
        result.Should().NotBeNull();
        result!.AskNl.Should().NotBeNull();
        result.AskNl!.Mode.Should().Be(NlMode.Free);
    }

    [Fact]
    public async Task TryDispatch_NlWithStyle_ReturnsRewrite()
    {
        _registry.Register(MakeEntry("nlsvc2", slashCommands: ["nl2"]));

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = true, Text = "data", UseNaturalLanguage = true, NaturalLanguageStyle = "be funny" }),
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        var result = await dispatcher.TryDispatchAsync(CmdCtx("/nl2", ""));
        result.Should().NotBeNull();
        result!.AskNl.Should().NotBeNull();
        result.AskNl!.Mode.Should().Be(NlMode.Rewrite);
        result.AskNl.Style.Should().Be("be funny");
    }

    [Fact]
    public async Task TryDispatch_EphemeralResponse_ReturnsEphemeral()
    {
        _registry.Register(MakeEntry("ephsvc", slashCommands: ["eph"]));

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = true, Text = "secret", Ephemeral = true, Markdown = true }),
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        var result = await dispatcher.TryDispatchAsync(CmdCtx("/eph", ""));
        result.Should().NotBeNull();
        result!.PassThrough.Should().NotBeNull();
        result.PassThrough!.Overrides!.Policy!.Ephemeral.Should().BeTrue();
    }

    [Fact]
    public async Task TryDispatch_EmptyResponse_ReturnsEmpty()
    {
        _registry.Register(MakeEntry("emptysvc", slashCommands: ["empty"]));

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = true }),
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        var result = await dispatcher.TryDispatchAsync(CmdCtx("/empty", ""));
        result.Should().NotBeNull();
        result!.PassThrough.Should().BeNull();
        result.AskNl.Should().BeNull();
    }

    [Fact]
    public async Task TryDispatch_PreservesReactionsAndSuppressMention()
    {
        _registry.Register(MakeEntry("reactsvc", slashCommands: ["react"]));

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = true, Text = "hi", SuppressMention = true, Reactions = new[] { "thumbsup" } }),
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        var result = await dispatcher.TryDispatchAsync(CmdCtx("/react", ""));
        result.Should().NotBeNull();
        result!.SuppressMention.Should().BeTrue();
        result.Reactions.Should().Contain("thumbsup");
    }

    // ── Argument extraction (tested via request to plugin) ──

    [Fact]
    public async Task TryDispatch_SubcommandArgs_ExtractsCorrectly()
    {
        _registry.Register(MakeEntry("export", subcommands: ["export"]));

        PluginExecuteRequest? captured = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStream();
            captured = JsonSerializer.Deserialize<PluginExecuteRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Success = true, Text = "ok" }),
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpFactory = new FakeHttpClientFactory(handler);
        var client = new PluginServiceClient(httpFactory, Options.Create(new PluginServiceOptions()), NullLogger<PluginServiceClient>.Instance);

        var channelOpts = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        channelOpts.CurrentValue.Returns(new ChannelPolicyOptions { AllowAll = true });
        var dispatcher = new RemotePluginDispatcher(_registry, client, new ChannelPolicy(channelOpts, NullLogger<ChannelPolicy>.Instance), NullLogger<RemotePluginDispatcher>.Instance);

        await dispatcher.TryDispatchAsync(CmdCtx("knutr", "export this #general"));

        captured.Should().NotBeNull();
        captured!.Subcommand.Should().Be("export");
        captured.Args.Should().BeEquivalentTo(["this", "#general"]);
    }
}
