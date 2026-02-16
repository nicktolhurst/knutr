using FluentAssertions;
using Knutr.Abstractions.Events;
using Knutr.Core.Orchestration;
using Xunit;

namespace Knutr.Tests.Core;

public class AddressingRulesTests
{
    private readonly AddressingRules _rules = new(
        BotDisplayName: "knutr",
        BotUserId: "U_BOT",
        Aliases: ["hey knutr", "yo bot"],
        ReplyInDMs: true,
        ReplyOnTag: true);

    private static MessageContext Msg(string text, string userId = "U_USER", string channelId = "C_TEST")
        => new("slack", "T1", channelId, userId, text);

    // ── ShouldRespond ──

    [Fact]
    public void ShouldRespond_BotMessage_ReturnsFalse()
    {
        _rules.ShouldRespond(Msg("hello", userId: "U_BOT")).Should().BeFalse();
    }

    [Fact]
    public void ShouldRespond_EmptyText_ReturnsFalse()
    {
        _rules.ShouldRespond(Msg("", userId: "U_USER")).Should().BeFalse();
    }

    [Fact]
    public void ShouldRespond_WhitespaceText_ReturnsFalse()
    {
        _rules.ShouldRespond(Msg("   ", userId: "U_USER")).Should().BeFalse();
    }

    [Fact]
    public void ShouldRespond_EmptyUserId_ReturnsFalse()
    {
        _rules.ShouldRespond(Msg("hello", userId: "")).Should().BeFalse();
    }

    [Fact]
    public void ShouldRespond_DmChannel_ReturnsTrue()
    {
        _rules.ShouldRespond(Msg("hello", channelId: "D_DM123")).Should().BeTrue();
    }

    [Fact]
    public void ShouldRespond_DmChannel_ReplyInDMsFalse_ReturnsFalse()
    {
        var rules = _rules with { ReplyInDMs = false };
        rules.ShouldRespond(Msg("hello", channelId: "D_DM123")).Should().BeFalse();
    }

    [Fact]
    public void ShouldRespond_SlackMention_ReturnsTrue()
    {
        _rules.ShouldRespond(Msg("<@U_BOT> hello")).Should().BeTrue();
    }

    [Fact]
    public void ShouldRespond_DisplayNameMention_ReturnsTrue()
    {
        _rules.ShouldRespond(Msg("@knutr hello")).Should().BeTrue();
    }

    [Fact]
    public void ShouldRespond_AliasMention_ReturnsTrue()
    {
        _rules.ShouldRespond(Msg("hey knutr what's up")).Should().BeTrue();
    }

    [Fact]
    public void ShouldRespond_NoMention_ReturnsFalse()
    {
        _rules.ShouldRespond(Msg("hello world")).Should().BeFalse();
    }

    [Fact]
    public void ShouldRespond_ReplyOnTagDisabled_ReturnsFalse()
    {
        var rules = _rules with { ReplyOnTag = false };
        rules.ShouldRespond(Msg("<@U_BOT> hello")).Should().BeFalse();
    }

    [Fact]
    public void ShouldRespond_MentionIsCaseInsensitive()
    {
        _rules.ShouldRespond(Msg("@KNUTR hello")).Should().BeTrue();
    }

    // ── ExtractTextWithoutMention ──

    [Fact]
    public void ExtractText_RemovesSlackMention()
    {
        _rules.ExtractTextWithoutMention("<@U_BOT> hello world").Should().Be("hello world");
    }

    [Fact]
    public void ExtractText_RemovesSlackMentionWithComma()
    {
        _rules.ExtractTextWithoutMention("<@U_BOT>, hello").Should().Be("hello");
    }

    [Fact]
    public void ExtractText_RemovesDisplayName()
    {
        _rules.ExtractTextWithoutMention("@knutr hello").Should().Be("hello");
    }

    [Fact]
    public void ExtractText_RemovesAlias()
    {
        _rules.ExtractTextWithoutMention("hey knutr what's up").Should().Be("what's up");
    }

    [Fact]
    public void ExtractText_TrimsResult()
    {
        _rules.ExtractTextWithoutMention("  <@U_BOT>  hello  ").Should().Be("hello");
    }

    [Fact]
    public void ExtractText_NoBotUserId_SkipsSlackMention()
    {
        var rules = _rules with { BotUserId = "" };
        rules.ExtractTextWithoutMention("<@U_BOT> hello").Should().Be("<@U_BOT> hello");
    }
}
