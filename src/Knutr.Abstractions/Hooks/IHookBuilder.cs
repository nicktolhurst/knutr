namespace Knutr.Abstractions.Hooks;

/// <summary>
/// Delegate for hook handlers.
/// </summary>
public delegate Task<HookResult> HookHandler(HookContext context, CancellationToken ct = default);

/// <summary>
/// Fluent builder for registering hooks.
/// </summary>
public interface IHookBuilder
{
    /// <summary>
    /// Registers a hook handler for a specific hook point and command pattern.
    /// </summary>
    /// <param name="point">The lifecycle point where this hook should run.</param>
    /// <param name="pattern">
    /// Pattern to match commands. Supports wildcards:
    /// - "knutr:deploy:*" matches any environment for deploy
    /// - "knutr:*:production" matches any action on production
    /// - "*:*:*" matches everything
    /// - "knutr:deploy:demo" exact match
    /// </param>
    /// <param name="handler">The async handler to execute.</param>
    /// <param name="priority">
    /// Execution priority (lower = earlier). Default is 0.
    /// Use negative values for early hooks, positive for late hooks.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    IHookBuilder On(HookPoint point, string pattern, HookHandler handler, int priority = 0);
}
