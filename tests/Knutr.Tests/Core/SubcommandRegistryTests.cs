using FluentAssertions;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Core.Orchestration;
using Xunit;

namespace Knutr.Tests.Core;

public class SubcommandRegistryTests
{
    private readonly SubcommandRegistry _registry = new();

    private static readonly SubcommandHandler DummyHandler = (_, _) =>
        Task.FromResult(PluginResult.Empty());

    // ── Register & TryGetHandler ──

    [Fact]
    public void Register_TryGetHandler_ReturnsTrue()
    {
        _registry.Register("knutr", "deploy", DummyHandler);
        _registry.TryGetHandler("knutr", "deploy", out var handler).Should().BeTrue();
        handler.Should().NotBeNull();
    }

    [Fact]
    public void TryGetHandler_NotRegistered_ReturnsFalse()
    {
        _registry.TryGetHandler("knutr", "nonexistent", out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetHandler_WrongParent_ReturnsFalse()
    {
        _registry.Register("knutr", "deploy", DummyHandler);
        _registry.TryGetHandler("other", "deploy", out _).Should().BeFalse();
    }

    // ── GetSubcommands ──

    [Fact]
    public void GetSubcommands_ReturnsRegistered()
    {
        _registry.Register("knutr", "deploy", DummyHandler);
        _registry.Register("knutr", "status", DummyHandler);
        _registry.GetSubcommands("knutr").Should().HaveCount(2);
        _registry.GetSubcommands("knutr").Should().Contain("deploy");
        _registry.GetSubcommands("knutr").Should().Contain("status");
    }

    [Fact]
    public void GetSubcommands_NoParent_ReturnsEmpty()
    {
        _registry.GetSubcommands("nonexistent").Should().BeEmpty();
    }

    // ── Builder interface ──

    [Fact]
    public void Subcommand_RegistersUnderKnutr()
    {
        _registry.Subcommand("test-cmd", DummyHandler);
        _registry.TryGetHandler("knutr", "test-cmd", out _).Should().BeTrue();
    }

    [Fact]
    public void Subcommand_Chainable()
    {
        _registry.Subcommand("a", DummyHandler).Subcommand("b", DummyHandler);
        _registry.GetSubcommands("knutr").Should().HaveCount(2);
    }

    // ── Case insensitive ──

    [Fact]
    public void Register_CaseInsensitive()
    {
        _registry.Register("Knutr", "Deploy", DummyHandler);
        _registry.TryGetHandler("knutr", "deploy", out _).Should().BeTrue();
    }

    [Fact]
    public void Register_TrimsWhitespace()
    {
        _registry.Register("  knutr  ", "  deploy  ", DummyHandler);
        _registry.TryGetHandler("knutr", "deploy", out _).Should().BeTrue();
    }

    // ── Overwrite ──

    [Fact]
    public void Register_SameSubcommand_Overwrites()
    {
        SubcommandHandler handler1 = (_, _) => Task.FromResult(PluginResult.Empty());
        SubcommandHandler handler2 = (_, _) => Task.FromResult(PluginResult.Empty());

        _registry.Register("knutr", "deploy", handler1);
        _registry.Register("knutr", "deploy", handler2);

        _registry.TryGetHandler("knutr", "deploy", out var handler).Should().BeTrue();
        handler.Should().Be(handler2);
    }

    // ── Handler execution ──

    [Fact]
    public async Task RegisteredHandler_CanExecute()
    {
        _registry.Register("knutr", "echo", (ctx, args) =>
            Task.FromResult(PluginResult.SkipNl(new Knutr.Abstractions.Replies.Reply("echo!"))));

        _registry.TryGetHandler("knutr", "echo", out var handler).Should().BeTrue();

        var ctx = new CommandContext("slack", "T1", "C1", "U1", "knutr", "echo hello");
        var result = await handler!(ctx, ["hello"]);
        result.PassThrough.Should().NotBeNull();
        result.PassThrough!.Reply.Text.Should().Be("echo!");
    }
}
