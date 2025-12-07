namespace Knutr.Tests.Adapters;

using System.Text.Json;
using FluentAssertions;
using Knutr.Adapters.Slack;
using Xunit;

public class SlackEventTranslatorTests
{
    #region TryParseMessage Tests

    [Fact]
    public void TryParseMessage_ValidMessageEvent_ReturnsTrue()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "event": {
                "type": "message",
                "channel": "C456",
                "user": "U789",
                "text": "hello world",
                "thread_ts": "1234567890.123456"
            }
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx.Should().NotBeNull();
        ctx!.Adapter.Should().Be("slack");
        ctx.TeamId.Should().Be("T123");
        ctx.ChannelId.Should().Be("C456");
        ctx.UserId.Should().Be("U789");
        ctx.Text.Should().Be("hello world");
        ctx.ThreadTs.Should().Be("1234567890.123456");
    }

    [Fact]
    public void TryParseMessage_MessageWithoutThread_ThreadTsIsNull()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "event": {
                "type": "message",
                "channel": "C456",
                "user": "U789",
                "text": "hello"
            }
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.ThreadTs.Should().BeNull();
    }

    [Fact]
    public void TryParseMessage_NoEventProperty_ReturnsFalse()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "other": "data"
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeFalse();
        ctx.Should().BeNull();
    }

    [Fact]
    public void TryParseMessage_WrongEventType_ReturnsFalse()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "event": {
                "type": "reaction_added",
                "channel": "C456",
                "user": "U789"
            }
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeFalse();
        ctx.Should().BeNull();
    }

    [Fact]
    public void TryParseMessage_MissingTeamId_UsesEmptyString()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "event": {
                "type": "message",
                "channel": "C456",
                "user": "U789",
                "text": "hello"
            }
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.TeamId.Should().BeEmpty();
    }

    [Fact]
    public void TryParseMessage_MissingUser_UsesEmptyString()
    {
        // Arrange - bot messages don't have a user field
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "event": {
                "type": "message",
                "channel": "C456",
                "text": "bot message"
            }
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.UserId.Should().BeEmpty();
    }

    [Fact]
    public void TryParseMessage_MissingText_UsesEmptyString()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "event": {
                "type": "message",
                "channel": "C456",
                "user": "U789"
            }
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.Text.Should().BeEmpty();
    }

    [Fact]
    public void TryParseMessage_ComplexText_PreservesFormatting()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "event": {
                "type": "message",
                "channel": "C456",
                "user": "U789",
                "text": "Hello <@U123> check out <https://example.com|this link>"
            }
        }
        """).RootElement;

        // Act
        SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        ctx!.Text.Should().Be("Hello <@U123> check out <https://example.com|this link>");
    }

    #endregion

    #region TryParseCommand Tests

    [Fact]
    public void TryParseCommand_ValidSlashCommand_ReturnsTrue()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "command": "/knutr",
            "team_id": "T123",
            "channel_id": "C456",
            "user_id": "U789",
            "text": "deploy production",
            "response_url": "https://hooks.slack.com/commands/T123/456/abc"
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseCommand(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx.Should().NotBeNull();
        ctx!.Adapter.Should().Be("slack");
        ctx.TeamId.Should().Be("T123");
        ctx.ChannelId.Should().Be("C456");
        ctx.UserId.Should().Be("U789");
        ctx.Command.Should().Be("/knutr");
        ctx.RawText.Should().Be("deploy production");
        ctx.ResponseUrl.Should().Be("https://hooks.slack.com/commands/T123/456/abc");
    }

    [Fact]
    public void TryParseCommand_NoCommandProperty_ReturnsFalse()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "channel_id": "C456",
            "user_id": "U789",
            "text": "hello"
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseCommand(json, out var ctx);

        // Assert
        result.Should().BeFalse();
        ctx.Should().BeNull();
    }

    [Fact]
    public void TryParseCommand_EmptyCommand_ReturnsTrue()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "command": "",
            "team_id": "T123",
            "channel_id": "C456",
            "user_id": "U789",
            "text": ""
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseCommand(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.Command.Should().BeEmpty();
    }

    [Fact]
    public void TryParseCommand_MissingOptionalFields_UsesEmptyStrings()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "command": "/ping"
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseCommand(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.Command.Should().Be("/ping");
        ctx.TeamId.Should().BeEmpty();
        ctx.ChannelId.Should().BeEmpty();
        ctx.UserId.Should().BeEmpty();
        ctx.RawText.Should().BeEmpty();
        ctx.ResponseUrl.Should().BeNull();
    }

    [Fact]
    public void TryParseCommand_NoResponseUrl_ReturnsNullResponseUrl()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "command": "/ping",
            "team_id": "T123",
            "channel_id": "C456",
            "user_id": "U789",
            "text": ""
        }
        """).RootElement;

        // Act
        SlackEventTranslator.TryParseCommand(json, out var ctx);

        // Assert
        ctx!.ResponseUrl.Should().BeNull();
    }

    [Fact]
    public void TryParseCommand_TextWithSpecialCharacters_PreservesText()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "command": "/knutr",
            "team_id": "T123",
            "channel_id": "C456",
            "user_id": "U789",
            "text": "deploy feature/my-branch --env=staging"
        }
        """).RootElement;

        // Act
        SlackEventTranslator.TryParseCommand(json, out var ctx);

        // Assert
        ctx!.RawText.Should().Be("deploy feature/my-branch --env=staging");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void TryParseMessage_NullJsonValues_HandlesGracefully()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "team_id": null,
            "event": {
                "type": "message",
                "channel": "C456",
                "user": null,
                "text": null
            }
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.TeamId.Should().BeEmpty();
        ctx.UserId.Should().BeEmpty();
        ctx.Text.Should().BeEmpty();
    }

    [Fact]
    public void TryParseCommand_NullJsonValues_HandlesGracefully()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "command": "/test",
            "team_id": null,
            "channel_id": null,
            "user_id": null,
            "text": null
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseCommand(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.Command.Should().Be("/test");
        ctx.TeamId.Should().BeEmpty();
        ctx.ChannelId.Should().BeEmpty();
        ctx.UserId.Should().BeEmpty();
        ctx.RawText.Should().BeEmpty();
    }

    [Fact]
    public void TryParseMessage_SubtypeMessage_StillParses()
    {
        // Arrange - message_changed events still have type "message"
        var json = JsonDocument.Parse("""
        {
            "team_id": "T123",
            "event": {
                "type": "message",
                "subtype": "message_changed",
                "channel": "C456",
                "user": "U789",
                "text": "edited message"
            }
        }
        """).RootElement;

        // Act
        var result = SlackEventTranslator.TryParseMessage(json, out var ctx);

        // Assert
        result.Should().BeTrue();
        ctx!.Text.Should().Be("edited message");
    }

    #endregion
}
