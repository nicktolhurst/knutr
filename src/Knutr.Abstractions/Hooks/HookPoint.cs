namespace Knutr.Abstractions.Hooks;

/// <summary>
/// Defines the points in the command lifecycle where hooks can execute.
/// </summary>
public enum HookPoint
{
    /// <summary>
    /// Runs before execution to validate the request.
    /// Can short-circuit execution by returning a rejection.
    /// Use for: permission checks, environment availability, input validation.
    /// </summary>
    Validate,

    /// <summary>
    /// Runs after validation passes but before the main handler executes.
    /// Use for: acquiring locks, audit logging, state preparation.
    /// </summary>
    BeforeExecute,

    /// <summary>
    /// Runs after the main handler completes successfully.
    /// Use for: cleanup, notifications, scheduling follow-up tasks.
    /// </summary>
    AfterExecute,

    /// <summary>
    /// Runs if the main handler or any hook throws an exception.
    /// Use for: error logging, cleanup, alerting.
    /// </summary>
    OnError
}
