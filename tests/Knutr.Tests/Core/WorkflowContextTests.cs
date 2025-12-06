namespace Knutr.Tests.Core;

using FluentAssertions;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Workflows;
using Knutr.Core.Replies;
using Knutr.Core.Workflows;
using NSubstitute;
using Xunit;

public class WorkflowContextTests
{
    private readonly IReplyService _replyService;
    private readonly IThreadedMessagingService _messagingService;
    private readonly WorkflowContext _sut;

    public WorkflowContextTests()
    {
        _replyService = Substitute.For<IReplyService>();
        _messagingService = Substitute.For<IThreadedMessagingService>();

        var commandContext = new CommandContext(
            UserId: "U123",
            ChannelId: "C456",
            EventId: "evt-1",
            TriggerId: null,
            ResponseUrl: null,
            ThreadTs: null,
            Source: EventSource.SlackMessage);

        _sut = new WorkflowContext(
            workflowId: "wf_test123",
            workflowName: "test:workflow",
            commandContext: commandContext,
            replyService: _replyService,
            messagingService: _messagingService,
            initialState: null);
    }

    #region Threading Tests

    [Fact]
    public async Task SendAsync_FirstMessage_PostsToChannelAndEstablishesThread()
    {
        // Arrange
        _messagingService.PostMessageAsync("C456", "Hello world", null, Arg.Any<CancellationToken>())
            .Returns("1234567890.123456");

        // Act
        await _sut.SendAsync("Hello world");

        // Assert
        await _messagingService.Received(1).PostMessageAsync("C456", "Hello world", null, Arg.Any<CancellationToken>());
        _sut.ThreadTs.Should().Be("1234567890.123456");
    }

    [Fact]
    public async Task SendAsync_SecondMessage_RepliesInThread()
    {
        // Arrange
        _messagingService.PostMessageAsync("C456", "First message", null, Arg.Any<CancellationToken>())
            .Returns("1234567890.123456");

        // Act
        await _sut.SendAsync("First message");
        await _sut.SendAsync("Second message");

        // Assert
        await _messagingService.Received(1).PostMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _replyService.Received(1).SendAsync(Arg.Any<Abstractions.Replies.Reply>(), Arg.Any<ReplyHandle>(), Arg.Any<ResponseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WhenPostFails_FallsBackToReplyService()
    {
        // Arrange
        _messagingService.PostMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // Act
        await _sut.SendAsync("Hello world");

        // Assert
        await _replyService.Received(1).SendAsync(Arg.Any<Abstractions.Replies.Reply>(), Arg.Any<ReplyHandle>(), Arg.Any<ResponseMode>(), Arg.Any<CancellationToken>());
        _sut.ThreadTs.Should().BeNull();
    }

    #endregion

    #region State Management Tests

    [Fact]
    public void Set_And_Get_ReturnsValue()
    {
        // Act
        _sut.Set("key", "value");
        var result = _sut.Get<string>("key");

        // Assert
        result.Should().Be("value");
    }

    [Fact]
    public void Get_NonExistentKey_ReturnsDefault()
    {
        // Act
        var result = _sut.Get<string>("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Has_ExistingKey_ReturnsTrue()
    {
        // Arrange
        _sut.Set("key", "value");

        // Act & Assert
        _sut.Has("key").Should().BeTrue();
    }

    [Fact]
    public void Has_NonExistentKey_ReturnsFalse()
    {
        // Act & Assert
        _sut.Has("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void GetState_ReturnsAllState()
    {
        // Arrange
        _sut.Set("key1", "value1");
        _sut.Set("key2", 42);

        // Act
        var state = _sut.GetState();

        // Assert
        state.Should().HaveCount(2);
        state["key1"].Should().Be("value1");
        state["key2"].Should().Be(42);
    }

    #endregion

    #region Initial State Tests

    [Fact]
    public void Constructor_WithInitialState_PopulatesState()
    {
        // Arrange
        var initialState = new Dictionary<string, object>
        {
            ["branch"] = "main",
            ["environment"] = "demo"
        };

        var commandContext = new CommandContext(
            UserId: "U123",
            ChannelId: "C456",
            EventId: "evt-1",
            TriggerId: null,
            ResponseUrl: null,
            ThreadTs: null,
            Source: EventSource.SlackMessage);

        // Act
        var context = new WorkflowContext(
            workflowId: "wf_test",
            workflowName: "test",
            commandContext: commandContext,
            replyService: _replyService,
            messagingService: _messagingService,
            initialState: initialState);

        // Assert
        context.Get<string>("branch").Should().Be("main");
        context.Get<string>("environment").Should().Be("demo");
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void Properties_AreSetCorrectly()
    {
        // Assert
        _sut.WorkflowId.Should().Be("wf_test123");
        _sut.WorkflowName.Should().Be("test:workflow");
        _sut.UserId.Should().Be("U123");
        _sut.ChannelId.Should().Be("C456");
        _sut.Status.Should().Be(WorkflowStatus.Running);
        _sut.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public void Cancel_SetsStatusAndCancellationToken()
    {
        // Act
        _sut.Cancel("Test cancellation");

        // Assert
        _sut.Status.Should().Be(WorkflowStatus.Cancelled);
        _sut.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Fail_SetsStatusAndErrorMessage()
    {
        // Act
        _sut.Fail("Something went wrong");

        // Assert
        _sut.Status.Should().Be(WorkflowStatus.Failed);
        _sut.ErrorMessage.Should().Be("Something went wrong");
        _sut.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task UpdateAsync_CallsMessagingService()
    {
        // Act
        await _sut.UpdateAsync("1234.5678", "Updated message");

        // Assert
        await _messagingService.Received(1).UpdateMessageAsync("C456", "1234.5678", "Updated message", Arg.Any<CancellationToken>());
    }

    #endregion
}
