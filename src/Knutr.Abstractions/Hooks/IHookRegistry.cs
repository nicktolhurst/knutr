namespace Knutr.Abstractions.Hooks;

/// <summary>
/// Registry for looking up and executing hooks.
/// </summary>
public interface IHookRegistry : IHookBuilder
{
    /// <summary>
    /// Executes all matching hooks for the given point and context.
    /// </summary>
    /// <param name="point">The hook point to execute.</param>
    /// <param name="context">The shared context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The aggregate result. If any hook rejects, returns that rejection.
    /// Otherwise returns Ok.
    /// </returns>
    Task<HookResult> ExecuteAsync(HookPoint point, HookContext context, CancellationToken ct = default);

    /// <summary>
    /// Gets the number of registered hooks for a specific point.
    /// </summary>
    int CountHooks(HookPoint point);
}
