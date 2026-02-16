using FluentAssertions;
using Knutr.Core.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Knutr.Tests.Core;

public class ChannelPolicyTests
{
    private static ChannelPolicy CreatePolicy(ChannelPolicyOptions options)
    {
        var monitor = Substitute.For<IOptionsMonitor<ChannelPolicyOptions>>();
        monitor.CurrentValue.Returns(options);
        return new ChannelPolicy(monitor, NullLogger<ChannelPolicy>.Instance);
    }

    private static ChannelPolicyOptions AllowAllOptions() => new() { AllowAll = true };

    private static ChannelPolicyOptions AllowlistOptions(params (string channelId, string[] plugins)[] channels)
    {
        var opts = new ChannelPolicyOptions { AllowAll = false };
        foreach (var (channelId, plugins) in channels)
            opts.Allowlist[channelId] = new ChannelConfig { Plugins = [.. plugins] };
        return opts;
    }

    // ── IsChannelAllowed ──

    [Fact]
    public void IsChannelAllowed_AllowAll_ReturnsTrue()
    {
        var policy = CreatePolicy(AllowAllOptions());
        policy.IsChannelAllowed("C_ANY").Should().BeTrue();
    }

    [Fact]
    public void IsChannelAllowed_DmChannel_AlwaysTrue()
    {
        var policy = CreatePolicy(AllowlistOptions());
        policy.IsChannelAllowed("D_DM123").Should().BeTrue();
    }

    [Fact]
    public void IsChannelAllowed_InAllowlist_ReturnsTrue()
    {
        var policy = CreatePolicy(AllowlistOptions(("C_OK", ["sentinel"])));
        policy.IsChannelAllowed("C_OK").Should().BeTrue();
    }

    [Fact]
    public void IsChannelAllowed_NotInAllowlist_ReturnsFalse()
    {
        var policy = CreatePolicy(AllowlistOptions(("C_OK", ["sentinel"])));
        policy.IsChannelAllowed("C_OTHER").Should().BeFalse();
    }

    // ── IsPluginEnabled ──

    [Fact]
    public void IsPluginEnabled_AllowAll_ReturnsTrue()
    {
        var policy = CreatePolicy(AllowAllOptions());
        policy.IsPluginEnabled("C_ANY", "sentinel").Should().BeTrue();
    }

    [Fact]
    public void IsPluginEnabled_DmChannel_AlwaysTrue()
    {
        var policy = CreatePolicy(AllowlistOptions());
        policy.IsPluginEnabled("D_DM123", "sentinel").Should().BeTrue();
    }

    [Fact]
    public void IsPluginEnabled_ChannelNotInAllowlist_ReturnsFalse()
    {
        var policy = CreatePolicy(AllowlistOptions(("C_OK", ["sentinel"])));
        policy.IsPluginEnabled("C_OTHER", "sentinel").Should().BeFalse();
    }

    [Fact]
    public void IsPluginEnabled_PluginInConfig_ReturnsTrue()
    {
        var policy = CreatePolicy(AllowlistOptions(("C_OK", ["sentinel", "joke"])));
        policy.IsPluginEnabled("C_OK", "sentinel").Should().BeTrue();
    }

    [Fact]
    public void IsPluginEnabled_PluginNotInConfig_ReturnsFalse()
    {
        var policy = CreatePolicy(AllowlistOptions(("C_OK", ["joke"])));
        policy.IsPluginEnabled("C_OK", "sentinel").Should().BeFalse();
    }

    [Fact]
    public void IsPluginEnabled_CaseInsensitive()
    {
        var policy = CreatePolicy(AllowlistOptions(("C_OK", ["Sentinel"])));
        policy.IsPluginEnabled("C_OK", "sentinel").Should().BeTrue();
    }

    // ── GetEnabledPlugins ──

    [Fact]
    public void GetEnabledPlugins_AllowAll_ReturnsEmpty()
    {
        var policy = CreatePolicy(AllowAllOptions());
        policy.GetEnabledPlugins("C_ANY").Should().BeEmpty();
    }

    [Fact]
    public void GetEnabledPlugins_DmChannel_ReturnsEmpty()
    {
        var policy = CreatePolicy(AllowlistOptions());
        policy.GetEnabledPlugins("D_DM123").Should().BeEmpty();
    }

    [Fact]
    public void GetEnabledPlugins_ReturnsConfigured()
    {
        var policy = CreatePolicy(AllowlistOptions(("C_OK", ["sentinel", "joke"])));
        policy.GetEnabledPlugins("C_OK").Should().HaveCount(2);
        policy.GetEnabledPlugins("C_OK").Should().Contain("sentinel");
        policy.GetEnabledPlugins("C_OK").Should().Contain("joke");
    }

    [Fact]
    public void GetEnabledPlugins_ChannelNotInAllowlist_ReturnsEmpty()
    {
        var policy = CreatePolicy(AllowlistOptions(("C_OK", ["sentinel"])));
        policy.GetEnabledPlugins("C_OTHER").Should().BeEmpty();
    }
}
