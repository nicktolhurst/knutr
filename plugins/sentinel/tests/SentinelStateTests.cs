using FluentAssertions;
using Xunit;

namespace Knutr.Plugins.Sentinel.Tests;

public class SentinelStateTests
{
    private readonly SentinelState _state = new();

    [Fact]
    public void Config_DefaultValues()
    {
        _state.Threshold.Should().Be(0.7);
        _state.Playful.Should().BeFalse();
        _state.BufferSize.Should().Be(20);
    }

    [Fact]
    public void SetConfig_UpdatesValue()
    {
        _state.SetConfig("threshold", "0.5");
        _state.GetConfig("threshold").Should().Be("0.5");
    }

    [Fact]
    public void WatchThread_IsTracked()
    {
        _state.WatchThread("C1", "T1", "hello", "U1");
        _state.IsThreadWatched("C1", "T1").Should().BeTrue();
    }

    [Fact]
    public void UnwatchThread_RemovesTracking()
    {
        _state.WatchThread("C1", "T1", "hello", "U1");
        _state.UnwatchThread("C1", "T1");
        _state.IsThreadWatched("C1", "T1").Should().BeFalse();
    }

    [Fact]
    public void WatchChannel_IsTracked()
    {
        _state.WatchChannel("C1", "U1");
        _state.IsChannelWatched("C1").Should().BeTrue();
    }

    [Fact]
    public void BufferMessage_StoresMessages()
    {
        _state.WatchThread("C1", "T1", "hello", "U1");
        _state.BufferMessage("C1", "T1", "U1", "test message");
        _state.GetBuffer("C1", "T1").Should().ContainSingle()
            .Which.Text.Should().Be("test message");
    }

    [Fact]
    public void BufferMessage_EnforcesMaxSize()
    {
        _state.SetConfig("buffer_size", "3");
        _state.WatchThread("C1", "T1", "hello", "U1");

        for (var i = 0; i < 5; i++)
            _state.BufferMessage("C1", "T1", "U1", $"msg{i}");

        var buffer = _state.GetBuffer("C1", "T1");
        buffer.Should().HaveCount(3);
        buffer[0].Text.Should().Be("msg2");
    }

    [Fact]
    public void RecordTopic_StoresAndUpdates()
    {
        _state.RecordTopic("C1", "T1", "Initial topic");
        _state.GetTopicHistory("C1").Should().ContainSingle()
            .Which.TopicSummary.Should().Be("Initial topic");

        _state.RecordTopic("C1", "T1", "Updated topic");
        _state.GetTopicHistory("C1").Should().ContainSingle()
            .Which.TopicSummary.Should().Be("Updated topic");
    }
}
