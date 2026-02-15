namespace Knutr.Tests.Core;

using FluentAssertions;
using Knutr.Core.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class InMemoryEventBusTests
{
    private readonly InMemoryEventBus _sut;

    public InMemoryEventBusTests()
    {
        _sut = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
    }

    [Fact]
    public void Publish_WithSubscriber_InvokesHandler()
    {
        // Arrange
        TestEvent? receivedEvent = null;
        _sut.Subscribe<TestEvent>((evt, _) =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });

        var testEvent = new TestEvent("test message");

        // Act
        _sut.Publish(testEvent);

        // Assert - async dispatch, give it a moment
        Thread.Sleep(50);
        receivedEvent.Should().NotBeNull();
        receivedEvent!.Message.Should().Be("test message");
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        // Arrange
        var testEvent = new TestEvent("test");

        // Act
        var action = () => _sut.Publish(testEvent);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void Publish_WithMultipleSubscribers_InvokesAllHandlers()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;

        _sut.Subscribe<TestEvent>((_, _) =>
        {
            handler1Called = true;
            return Task.CompletedTask;
        });

        _sut.Subscribe<TestEvent>((_, _) =>
        {
            handler2Called = true;
            return Task.CompletedTask;
        });

        // Act
        _sut.Publish(new TestEvent("test"));

        // Assert
        Thread.Sleep(50);
        handler1Called.Should().BeTrue();
        handler2Called.Should().BeTrue();
    }

    [Fact]
    public void Subscribe_DifferentTypes_RoutesToCorrectHandler()
    {
        // Arrange
        TestEvent? receivedTest = null;
        OtherEvent? receivedOther = null;

        _sut.Subscribe<TestEvent>((evt, _) =>
        {
            receivedTest = evt;
            return Task.CompletedTask;
        });

        _sut.Subscribe<OtherEvent>((evt, _) =>
        {
            receivedOther = evt;
            return Task.CompletedTask;
        });

        // Act
        _sut.Publish(new TestEvent("test"));

        // Assert
        Thread.Sleep(50);
        receivedTest.Should().NotBeNull();
        receivedOther.Should().BeNull();
    }

    [Fact]
    public void Publish_MultipleTimes_InvokesHandlerEachTime()
    {
        // Arrange
        var callCount = 0;
        _sut.Subscribe<TestEvent>((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        // Act
        _sut.Publish(new TestEvent("1"));
        _sut.Publish(new TestEvent("2"));
        _sut.Publish(new TestEvent("3"));

        // Assert
        Thread.Sleep(100);
        callCount.Should().Be(3);
    }

    [Fact]
    public void Subscribe_SameHandlerMultipleTimes_InvokesMultipleTimes()
    {
        // Arrange
        var callCount = 0;
        Func<TestEvent, CancellationToken, Task> handler = (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        };

        _sut.Subscribe(handler);
        _sut.Subscribe(handler);

        // Act
        _sut.Publish(new TestEvent("test"));

        // Assert
        Thread.Sleep(50);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Subscribe_HandlerReceivesCorrectEventData()
    {
        // Arrange
        var tcs = new TaskCompletionSource<ComplexEvent>();
        _sut.Subscribe<ComplexEvent>((evt, _) =>
        {
            tcs.SetResult(evt);
            return Task.CompletedTask;
        });

        var complexEvent = new ComplexEvent(42, "complex", new[] { "a", "b", "c" });

        // Act
        _sut.Publish(complexEvent);

        // Assert
        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        received.Id.Should().Be(42);
        received.Name.Should().Be("complex");
        received.Items.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void Publish_HandlerThrows_DoesNotAffectOtherHandlers()
    {
        // Arrange
        var handler2Called = false;

        _sut.Subscribe<TestEvent>((_, _) => throw new InvalidOperationException("handler error"));

        _sut.Subscribe<TestEvent>((_, _) =>
        {
            handler2Called = true;
            return Task.CompletedTask;
        });

        // Act
        _sut.Publish(new TestEvent("test"));

        // Assert - second handler should still be called even if first throws
        Thread.Sleep(50);
        handler2Called.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_IsFireAndForget_ReturnsImmediately()
    {
        // Arrange
        _sut.Subscribe<TestEvent>(async (_, _) =>
        {
            await Task.Delay(1000);
        });

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _sut.Publish(new TestEvent("test"));
        sw.Stop();

        // Assert - should return immediately, not wait for handler
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    // Test event types
    private sealed record TestEvent(string Message);
    private sealed record OtherEvent(int Value);
    private sealed record ComplexEvent(int Id, string Name, string[] Items);
}
