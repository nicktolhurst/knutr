namespace Knutr.Tests.Core;

using FluentAssertions;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Hooks;
using Knutr.Core.Hooks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class HookRegistryTests
{
    private readonly HookRegistry _sut;

    public HookRegistryTests()
    {
        _sut = new HookRegistry(NullLogger<HookRegistry>.Instance);
    }

    [Fact]
    public void On_RegistersHook_IncrementsCount()
    {
        // Arrange & Act
        _sut.On(HookPoint.Validate, "knutr:*", (_, _) => Task.FromResult(HookResult.Ok()));

        // Assert
        _sut.CountHooks(HookPoint.Validate).Should().Be(1);
        _sut.CountHooks(HookPoint.BeforeExecute).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NoMatchingHooks_ReturnsOk()
    {
        // Arrange
        _sut.On(HookPoint.Validate, "other:*", (_, _) => Task.FromResult(HookResult.Reject("blocked")));
        var context = CreateHookContext("knutr", "deploy");

        // Act
        var result = await _sut.ExecuteAsync(HookPoint.Validate, context);

        // Assert
        result.Continue.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_MatchingHook_ExecutesHandler()
    {
        // Arrange
        var executed = false;
        _sut.On(HookPoint.Validate, "knutr:*", (_, _) =>
        {
            executed = true;
            return Task.FromResult(HookResult.Ok());
        });

        var context = CreateHookContext("knutr", "deploy");

        // Act
        await _sut.ExecuteAsync(HookPoint.Validate, context);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RejectedHook_StopsPipeline()
    {
        // Arrange
        _sut.On(HookPoint.Validate, "knutr:**", (_, _) =>
            Task.FromResult(HookResult.Reject("environment claimed")));

        var context = CreateHookContext("knutr", "deploy");

        // Act
        var result = await _sut.ExecuteAsync(HookPoint.Validate, context);

        // Assert
        result.Continue.Should().BeFalse();
        result.ErrorMessage.Should().Be("environment claimed");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleHooks_ExecutesInPriorityOrder()
    {
        // Arrange
        var executionOrder = new List<int>();

        _sut.On(HookPoint.BeforeExecute, "knutr:**", (_, _) =>
        {
            executionOrder.Add(2);
            return Task.FromResult(HookResult.Ok());
        }, priority: 10);

        _sut.On(HookPoint.BeforeExecute, "knutr:**", (_, _) =>
        {
            executionOrder.Add(1);
            return Task.FromResult(HookResult.Ok());
        }, priority: 5);

        _sut.On(HookPoint.BeforeExecute, "knutr:**", (_, _) =>
        {
            executionOrder.Add(3);
            return Task.FromResult(HookResult.Ok());
        }, priority: 15);

        var context = CreateHookContext("knutr", "deploy");

        // Act
        await _sut.ExecuteAsync(HookPoint.BeforeExecute, context);

        // Assert
        executionOrder.Should().BeEquivalentTo([1, 2, 3], opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task ExecuteAsync_FirstHookRejects_DoesNotExecuteRemainingHooks()
    {
        // Arrange
        var secondExecuted = false;

        _sut.On(HookPoint.Validate, "knutr:**", (_, _) =>
            Task.FromResult(HookResult.Reject("blocked")), priority: 1);

        _sut.On(HookPoint.Validate, "knutr:**", (_, _) =>
        {
            secondExecuted = true;
            return Task.FromResult(HookResult.Ok());
        }, priority: 2);

        var context = CreateHookContext("knutr", "deploy");

        // Act
        await _sut.ExecuteAsync(HookPoint.Validate, context);

        // Assert
        secondExecuted.Should().BeFalse();
    }

    [Theory]
    [InlineData("knutr:deploy", "knutr:deploy", true)]
    [InlineData("knutr:*", "knutr:deploy", true)]
    [InlineData("knutr:*", "knutr:build", true)]
    [InlineData("knutr:**", "knutr:deploy:production", true)]
    [InlineData("*:deploy", "knutr:deploy", true)]
    [InlineData("other:*", "knutr:deploy", false)]
    [InlineData("knutr:build", "knutr:deploy", false)]
    public async Task ExecuteAsync_PatternMatching_CorrectlyMatchesPatterns(
        string pattern, string commandAction, bool shouldMatch)
    {
        // Arrange
        var executed = false;
        _sut.On(HookPoint.Validate, pattern, (_, _) =>
        {
            executed = true;
            return Task.FromResult(HookResult.Ok());
        });

        var parts = commandAction.Split(':');
        var context = CreateHookContext(parts[0], parts.Length > 1 ? parts[1] : null);

        // Act
        await _sut.ExecuteAsync(HookPoint.Validate, context);

        // Assert
        executed.Should().Be(shouldMatch);
    }

    [Fact]
    public async Task ExecuteAsync_PatternIsCaseInsensitive()
    {
        // Arrange
        var executed = false;
        _sut.On(HookPoint.Validate, "KNUTR:DEPLOY", (_, _) =>
        {
            executed = true;
            return Task.FromResult(HookResult.Ok());
        });

        var context = CreateHookContext("knutr", "deploy");

        // Act
        await _sut.ExecuteAsync(HookPoint.Validate, context);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_HookThrowsException_PropagatesException()
    {
        // Arrange
        _sut.On(HookPoint.BeforeExecute, "knutr:**", (_, _) =>
            throw new InvalidOperationException("test error"));

        var context = CreateHookContext("knutr", "deploy");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExecuteAsync(HookPoint.BeforeExecute, context));
    }

    [Fact]
    public async Task ExecuteAsync_OnErrorHookThrows_DoesNotRethrow()
    {
        // Arrange
        _sut.On(HookPoint.OnError, "knutr:**", (_, _) =>
            throw new InvalidOperationException("error handler failed"));

        var context = CreateHookContext("knutr", "deploy");

        // Act
        var result = await _sut.ExecuteAsync(HookPoint.OnError, context);

        // Assert - should not throw, just return Ok
        result.Continue.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCancelledException()
    {
        // Arrange
        _sut.On(HookPoint.Validate, "knutr:**", async (_, ct) =>
        {
            await Task.Delay(1000, ct);
            return HookResult.Ok();
        });

        var context = CreateHookContext("knutr", "deploy");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.ExecuteAsync(HookPoint.Validate, context, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_WithEnvironmentArgument_IncludesInCommandKey()
    {
        // Arrange
        var executed = false;
        _sut.On(HookPoint.Validate, "knutr:deploy:production", (_, _) =>
        {
            executed = true;
            return Task.FromResult(HookResult.Ok());
        });

        var context = CreateHookContext("knutr", "deploy", new Dictionary<string, object?>
        {
            ["environment"] = "production"
        });

        // Act
        await _sut.ExecuteAsync(HookPoint.Validate, context);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public void On_ReturnsItself_AllowsChaining()
    {
        // Act
        var result = _sut
            .On(HookPoint.Validate, "a:*", (_, _) => Task.FromResult(HookResult.Ok()))
            .On(HookPoint.BeforeExecute, "b:*", (_, _) => Task.FromResult(HookResult.Ok()));

        // Assert
        result.Should().BeSameAs(_sut);
        _sut.CountHooks(HookPoint.Validate).Should().Be(1);
        _sut.CountHooks(HookPoint.BeforeExecute).Should().Be(1);
    }

    private static HookContext CreateHookContext(
        string command,
        string? action = null,
        Dictionary<string, object?>? arguments = null)
    {
        return new HookContext
        {
            PluginName = "TestPlugin",
            Command = command,
            Action = action,
            Arguments = arguments ?? new Dictionary<string, object?>(),
            CommandContext = new CommandContext(
                Adapter: "slack",
                TeamId: "T123",
                ChannelId: "C123",
                UserId: "U123",
                Command: command,
                RawText: $"{command} {action ?? ""}")
        };
    }
}
