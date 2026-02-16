using FluentAssertions;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Knutr.Core.Hooks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Knutr.Tests.Core;

public class HookPipelineTests
{
    private readonly HookRegistry _hooks = new(NullLogger<HookRegistry>.Instance);
    private readonly HookPipeline _pipeline;

    private static readonly CommandContext TestCmd = new("slack", "T1", "C1", "U1", "knutr", "deploy prod");

    public HookPipelineTests()
    {
        _pipeline = new HookPipeline(_hooks, NullLogger<HookPipeline>.Instance);
    }

    private Task<PluginResult> Execute(Func<Task<PluginResult>> handler)
        => _pipeline.ExecuteAsync("test-plugin", "knutr", "deploy", new Dictionary<string, object?>(), TestCmd, handler);

    // ── Basic execution ──

    [Fact]
    public async Task Execute_NoHooks_RunsHandler()
    {
        var result = await Execute(() => Task.FromResult(PluginResult.SkipNl(new Reply("done"))));
        result.PassThrough.Should().NotBeNull();
        result.PassThrough!.Reply.Text.Should().Be("done");
    }

    [Fact]
    public async Task Execute_HandlerReturnsEmpty_ReturnsEmpty()
    {
        var result = await Execute(() => Task.FromResult(PluginResult.Empty()));
        result.PassThrough.Should().BeNull();
        result.AskNl.Should().BeNull();
    }

    // ── Validate hooks ──

    [Fact]
    public async Task Execute_ValidateRejects_SkipsHandler()
    {
        _hooks.On(HookPoint.Validate, "**", (_, _) =>
            Task.FromResult(HookResult.Reject("not allowed")));

        var handlerCalled = false;
        var result = await Execute(() =>
        {
            handlerCalled = true;
            return Task.FromResult(PluginResult.Empty());
        });

        handlerCalled.Should().BeFalse();
        result.PassThrough.Should().NotBeNull();
        result.PassThrough!.Reply.Text.Should().Contain("not allowed");
    }

    [Fact]
    public async Task Execute_ValidateRejects_WithCustomResponse()
    {
        var customResult = PluginResult.Ephemeral("custom rejection");
        _hooks.On(HookPoint.Validate, "**", (_, _) =>
            Task.FromResult(HookResult.Respond(customResult)));

        var result = await Execute(() => Task.FromResult(PluginResult.Empty()));
        result.Should().BeSameAs(customResult);
    }

    [Fact]
    public async Task Execute_ValidatePasses_HandlerRuns()
    {
        _hooks.On(HookPoint.Validate, "**", (_, _) =>
            Task.FromResult(HookResult.Ok()));

        var handlerCalled = false;
        await Execute(() =>
        {
            handlerCalled = true;
            return Task.FromResult(PluginResult.Empty());
        });

        handlerCalled.Should().BeTrue();
    }

    // ── BeforeExecute hooks ──

    [Fact]
    public async Task Execute_BeforeRejects_SkipsHandler()
    {
        _hooks.On(HookPoint.BeforeExecute, "**", (_, _) =>
            Task.FromResult(HookResult.Reject("before failed")));

        var handlerCalled = false;
        var result = await Execute(() =>
        {
            handlerCalled = true;
            return Task.FromResult(PluginResult.Empty());
        });

        handlerCalled.Should().BeFalse();
        result.PassThrough!.Reply.Text.Should().Contain("before failed");
    }

    // ── AfterExecute hooks ──

    [Fact]
    public async Task Execute_AfterOverrides_ReturnsOverride()
    {
        var overrideResult = PluginResult.SkipNl(new Reply("overridden!"));
        _hooks.On(HookPoint.AfterExecute, "**", (_, _) =>
            Task.FromResult(HookResult.Respond(overrideResult)));

        var result = await Execute(() => Task.FromResult(PluginResult.SkipNl(new Reply("original"))));
        result.Should().BeSameAs(overrideResult);
    }

    [Fact]
    public async Task Execute_AfterContinues_ReturnsHandlerResult()
    {
        _hooks.On(HookPoint.AfterExecute, "**", (_, _) =>
            Task.FromResult(HookResult.Ok()));

        var result = await Execute(() => Task.FromResult(PluginResult.SkipNl(new Reply("original"))));
        result.PassThrough!.Reply.Text.Should().Be("original");
    }

    [Fact]
    public async Task Execute_AfterRejects_WithoutResponse_ReturnsHandlerResult()
    {
        // AfterExecute rejecting without a custom response should still return the original handler result
        _hooks.On(HookPoint.AfterExecute, "**", (_, _) =>
            Task.FromResult(HookResult.Reject("after error")));

        var result = await Execute(() => Task.FromResult(PluginResult.SkipNl(new Reply("original"))));
        result.PassThrough!.Reply.Text.Should().Be("original");
    }

    // ── OnError hooks ──

    [Fact]
    public async Task Execute_HandlerThrows_RunsOnErrorHook_ThenRethrows()
    {
        var onErrorCalled = false;
        _hooks.On(HookPoint.OnError, "**", (ctx, _) =>
        {
            onErrorCalled = true;
            ctx.Error.Should().NotBeNull();
            ctx.Error!.Message.Should().Be("boom");
            return Task.FromResult(HookResult.Ok());
        });

        var act = () => Execute(() => throw new InvalidOperationException("boom"));
        await act.Should().ThrowAsync<InvalidOperationException>();
        onErrorCalled.Should().BeTrue();
    }

    // ── Context flow ──

    [Fact]
    public async Task Execute_SetsContextResult_ForAfterHooks()
    {
        PluginResult? capturedResult = null;
        _hooks.On(HookPoint.AfterExecute, "**", (ctx, _) =>
        {
            capturedResult = ctx.Result;
            return Task.FromResult(HookResult.Ok());
        });

        var expected = PluginResult.SkipNl(new Reply("the result"));
        await Execute(() => Task.FromResult(expected));

        capturedResult.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Execute_ContextHasPluginInfo()
    {
        string? capturedPlugin = null;
        string? capturedCommand = null;
        string? capturedAction = null;

        _hooks.On(HookPoint.Validate, "**", (ctx, _) =>
        {
            capturedPlugin = ctx.PluginName;
            capturedCommand = ctx.Command;
            capturedAction = ctx.Action;
            return Task.FromResult(HookResult.Ok());
        });

        await Execute(() => Task.FromResult(PluginResult.Empty()));

        capturedPlugin.Should().Be("test-plugin");
        capturedCommand.Should().Be("knutr");
        capturedAction.Should().Be("deploy");
    }

    // ── Hook pipeline order ──

    [Fact]
    public async Task Execute_FullLifecycle_RunsInOrder()
    {
        var order = new List<string>();

        _hooks.On(HookPoint.Validate, "**", (_, _) =>
        {
            order.Add("validate");
            return Task.FromResult(HookResult.Ok());
        });

        _hooks.On(HookPoint.BeforeExecute, "**", (_, _) =>
        {
            order.Add("before");
            return Task.FromResult(HookResult.Ok());
        });

        _hooks.On(HookPoint.AfterExecute, "**", (_, _) =>
        {
            order.Add("after");
            return Task.FromResult(HookResult.Ok());
        });

        await Execute(() =>
        {
            order.Add("handler");
            return Task.FromResult(PluginResult.Empty());
        });

        order.Should().BeEquivalentTo(["validate", "before", "handler", "after"], o => o.WithStrictOrdering());
    }

    // ── Message context overload ──

    [Fact]
    public async Task Execute_WithMessageContext_Works()
    {
        var msgCtx = new MessageContext("slack", "T1", "C1", "U1", "hello world");

        var result = await _pipeline.ExecuteAsync(
            "test-plugin", "scan", null,
            new Dictionary<string, object?>(),
            msgCtx,
            () => Task.FromResult(PluginResult.SkipNl(new Reply("scanned"))));

        result.PassThrough!.Reply.Text.Should().Be("scanned");
    }
}
