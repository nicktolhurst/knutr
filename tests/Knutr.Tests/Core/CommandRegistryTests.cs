namespace Knutr.Tests.Core;

using FluentAssertions;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Plugins;
using Knutr.Core.Orchestration;
using Xunit;

public class CommandRegistryTests
{
    private readonly CommandRegistry _sut;

    public CommandRegistryTests()
    {
        _sut = new CommandRegistry();
    }

    #region Slash Command Tests

    [Fact]
    public void RegisterSlash_And_TryMatch_FindsCommand()
    {
        // Arrange
        var handler = CreateSlashHandler();
        _sut.RegisterSlash("/knutr", handler);
        var context = CreateCommandContext("/knutr");

        // Act
        var found = _sut.TryMatch(context, out var matchedHandler);

        // Assert
        found.Should().BeTrue();
        matchedHandler.Should().BeSameAs(handler);
    }

    [Fact]
    public void RegisterSlash_WithLeadingSlash_MatchesWithOrWithoutSlash()
    {
        // Arrange
        var handler = CreateSlashHandler();
        _sut.RegisterSlash("/knutr", handler);

        // Act & Assert - match with slash
        var withSlash = _sut.TryMatch(CreateCommandContext("/knutr"), out _);
        withSlash.Should().BeTrue();

        // Act & Assert - match without slash in registration
        _sut.RegisterSlash("ping", CreateSlashHandler());
        var withoutSlash = _sut.TryMatch(CreateCommandContext("ping"), out _);
        withoutSlash.Should().BeTrue();
    }

    [Fact]
    public void TryMatch_UnregisteredCommand_ReturnsFalse()
    {
        // Arrange
        _sut.RegisterSlash("/knutr", CreateSlashHandler());
        var context = CreateCommandContext("/unknown");

        // Act
        var found = _sut.TryMatch(context, out var handler);

        // Assert
        found.Should().BeFalse();
        handler.Should().BeNull();
    }

    [Fact]
    public void RegisterSlash_CaseInsensitive()
    {
        // Arrange
        var handler = CreateSlashHandler();
        _sut.RegisterSlash("/KNUTR", handler);

        // Act
        var found = _sut.TryMatch(CreateCommandContext("/knutr"), out _);

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public void RegisterSlash_OverwritesExistingHandler()
    {
        // Arrange
        var handler1 = CreateSlashHandler("result1");
        var handler2 = CreateSlashHandler("result2");

        _sut.RegisterSlash("/knutr", handler1);
        _sut.RegisterSlash("/knutr", handler2);

        // Act
        _sut.TryMatch(CreateCommandContext("/knutr"), out var matched);

        // Assert
        matched.Should().BeSameAs(handler2);
    }

    [Fact]
    public void RegisterSlash_TrimsWhitespace()
    {
        // Arrange
        _sut.RegisterSlash("  /knutr  ", CreateSlashHandler());

        // Act
        var found = _sut.TryMatch(CreateCommandContext("/knutr"), out _);

        // Assert
        found.Should().BeTrue();
    }

    #endregion

    #region Message Command Tests

    [Fact]
    public void RegisterMessage_And_TryMatch_FindsTrigger()
    {
        // Arrange
        var handler = CreateMessageHandler();
        _sut.RegisterMessage("ping", null, handler);
        var context = CreateMessageContext("ping");

        // Act
        var found = _sut.TryMatch(context, out var matched);

        // Assert
        found.Should().BeTrue();
        matched.Should().BeSameAs(handler);
    }

    [Fact]
    public void RegisterMessage_WithAliases_MatchesAllAliases()
    {
        // Arrange
        var handler = CreateMessageHandler();
        _sut.RegisterMessage("ping", ["pong", "p"], handler);

        // Act & Assert
        _sut.TryMatch(CreateMessageContext("ping"), out _).Should().BeTrue();
        _sut.TryMatch(CreateMessageContext("pong"), out _).Should().BeTrue();
        _sut.TryMatch(CreateMessageContext("p"), out _).Should().BeTrue();
    }

    [Fact]
    public void TryMatch_Message_MatchesFirstWordOnly()
    {
        // Arrange
        _sut.RegisterMessage("deploy", null, CreateMessageHandler());

        // Act
        var context = CreateMessageContext("deploy production");
        var found = _sut.TryMatch(context, out _);

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public void TryMatch_Message_CaseInsensitive()
    {
        // Arrange
        _sut.RegisterMessage("DEPLOY", null, CreateMessageHandler());

        // Act
        var found = _sut.TryMatch(CreateMessageContext("deploy"), out _);

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public void TryMatch_EmptyMessage_ReturnsFalse()
    {
        // Arrange
        _sut.RegisterMessage("ping", null, CreateMessageHandler());

        // Act
        var found = _sut.TryMatch(CreateMessageContext(""), out _);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public void TryMatch_WhitespaceOnlyMessage_ReturnsFalse()
    {
        // Arrange
        _sut.RegisterMessage("ping", null, CreateMessageHandler());

        // Act
        var found = _sut.TryMatch(CreateMessageContext("   "), out _);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public void RegisterMessage_NullAliases_DoesNotThrow()
    {
        // Arrange & Act
        var action = () => _sut.RegisterMessage("ping", null, CreateMessageHandler());

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void RegisterMessage_EmptyAliasArray_DoesNotThrow()
    {
        // Arrange & Act
        var action = () => _sut.RegisterMessage("ping", [], CreateMessageHandler());

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region ICommandBuilder Interface Tests

    [Fact]
    public void Slash_ReturnsItself_ForChaining()
    {
        // Act
        var result = _sut.Slash("/cmd1", CreateSlashHandler())
                         .Slash("/cmd2", CreateSlashHandler());

        // Assert
        result.Should().BeSameAs(_sut);
    }

    [Fact]
    public void Message_ReturnsItself_ForChaining()
    {
        // Act
        var result = _sut.Message("cmd1", null, CreateMessageHandler())
                         .Message("cmd2", ["alias"], CreateMessageHandler());

        // Assert
        result.Should().BeSameAs(_sut);
    }

    [Fact]
    public void ChainedRegistrations_AllWork()
    {
        // Arrange & Act
        _sut.Slash("/knutr", CreateSlashHandler())
            .Slash("/ping", CreateSlashHandler())
            .Message("deploy", ["d"], CreateMessageHandler())
            .Message("status", null, CreateMessageHandler());

        // Assert
        _sut.TryMatch(CreateCommandContext("/knutr"), out _).Should().BeTrue();
        _sut.TryMatch(CreateCommandContext("/ping"), out _).Should().BeTrue();
        _sut.TryMatch(CreateMessageContext("deploy"), out _).Should().BeTrue();
        _sut.TryMatch(CreateMessageContext("d"), out _).Should().BeTrue();
        _sut.TryMatch(CreateMessageContext("status"), out _).Should().BeTrue();
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task RegisterSlash_ThreadSafe_ConcurrentRegistrations()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => _sut.RegisterSlash($"/cmd{i}", CreateSlashHandler())));

        // Act
        await Task.WhenAll(tasks);

        // Assert - all should be registered
        for (var i = 0; i < 100; i++)
        {
            _sut.TryMatch(CreateCommandContext($"/cmd{i}"), out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task TryMatch_ThreadSafe_ConcurrentReads()
    {
        // Arrange
        _sut.RegisterSlash("/knutr", CreateSlashHandler());
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => _sut.TryMatch(CreateCommandContext("/knutr"), out _)));

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllBeEquivalentTo(true);
    }

    #endregion

    #region Helper Methods

    private static Func<CommandContext, Task<PluginResult>> CreateSlashHandler(string? response = null)
    {
        return _ => Task.FromResult(PluginResult.PassThrough(response ?? "test response"));
    }

    private static Func<MessageContext, Task<PluginResult>> CreateMessageHandler(string? response = null)
    {
        return _ => Task.FromResult(PluginResult.PassThrough(response ?? "test response"));
    }

    private static CommandContext CreateCommandContext(string command)
    {
        return new CommandContext(
            Adapter: "slack",
            TeamId: "T123",
            ChannelId: "C123",
            UserId: "U123",
            Command: command,
            RawText: command);
    }

    private static MessageContext CreateMessageContext(string text)
    {
        return new MessageContext(
            Adapter: "slack",
            TeamId: "T123",
            ChannelId: "C123",
            UserId: "U123",
            Text: text);
    }

    #endregion
}
