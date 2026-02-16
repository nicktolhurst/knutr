namespace Knutr.Core.Hooks;

using Knutr.Abstractions.Events;
using Knutr.Abstractions.Hooks;
using Knutr.Abstractions.Plugins;
using Knutr.Abstractions.Replies;
using Microsoft.Extensions.Logging;

/// <summary>
/// Executes command handlers wrapped in a hook pipeline.
/// </summary>
public sealed class HookPipeline(IHookRegistry hooks, ILogger<HookPipeline> log)
{

    /// <summary>
    /// Executes a command handler wrapped in the hook pipeline.
    /// </summary>
    public async Task<PluginResult> ExecuteAsync(
        string pluginName,
        string command,
        string? action,
        IReadOnlyDictionary<string, object?> arguments,
        CommandContext cmdCtx,
        Func<Task<PluginResult>> handler,
        CancellationToken ct = default)
    {
        var context = new HookContext
        {
            PluginName = pluginName,
            Command = command,
            Action = action,
            Arguments = arguments,
            CommandContext = cmdCtx
        };

        return await ExecuteCoreAsync(context, handler, ct);
    }

    /// <summary>
    /// Executes a message handler wrapped in the hook pipeline.
    /// </summary>
    public async Task<PluginResult> ExecuteAsync(
        string pluginName,
        string command,
        string? action,
        IReadOnlyDictionary<string, object?> arguments,
        MessageContext msgCtx,
        Func<Task<PluginResult>> handler,
        CancellationToken ct = default)
    {
        var context = new HookContext
        {
            PluginName = pluginName,
            Command = command,
            Action = action,
            Arguments = arguments,
            MessageContext = msgCtx
        };

        return await ExecuteCoreAsync(context, handler, ct);
    }

    private async Task<PluginResult> ExecuteCoreAsync(
        HookContext context,
        Func<Task<PluginResult>> handler,
        CancellationToken ct)
    {
        try
        {
            // 1. Validate hooks (can reject)
            var validateResult = await hooks.ExecuteAsync(HookPoint.Validate, context, ct);
            if (!validateResult.Continue)
            {
                return validateResult.Response
                    ?? PluginResult.SkipNl(new Reply($":no_entry: {validateResult.ErrorMessage}", Markdown: true));
            }

            // 2. BeforeExecute hooks
            var beforeResult = await hooks.ExecuteAsync(HookPoint.BeforeExecute, context, ct);
            if (!beforeResult.Continue)
            {
                return beforeResult.Response
                    ?? PluginResult.SkipNl(new Reply($":warning: {beforeResult.ErrorMessage}", Markdown: true));
            }

            // 3. Execute main handler
            var result = await handler();
            context.Result = result;

            // 4. AfterExecute hooks
            var afterResult = await hooks.ExecuteAsync(HookPoint.AfterExecute, context, ct);
            if (!afterResult.Continue && afterResult.Response is not null)
            {
                // AfterExecute can override the response if desired
                return afterResult.Response;
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Error = ex;
            log.LogError(ex, "Error executing command {Command}:{Action}", context.Command, context.Action);

            // 5. OnError hooks
            await hooks.ExecuteAsync(HookPoint.OnError, context, ct);

            // Re-throw to let orchestrator handle it
            throw;
        }
    }
}
