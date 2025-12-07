namespace Knutr.Abstractions.Hooks;

using Knutr.Abstractions.Plugins;

/// <summary>
/// Result returned from a hook execution.
/// </summary>
public sealed class HookResult
{
    private HookResult() { }

    /// <summary>
    /// Whether to continue executing the pipeline.
    /// </summary>
    public bool Continue { get; private init; } = true;

    /// <summary>
    /// Error message if the hook rejected the request.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>
    /// Optional response to send instead of continuing execution.
    /// </summary>
    public PluginResult? Response { get; private init; }

    /// <summary>
    /// Continue execution normally.
    /// </summary>
    public static HookResult Ok() => new();

    /// <summary>
    /// Stop execution with an error message.
    /// </summary>
    public static HookResult Reject(string errorMessage) => new()
    {
        Continue = false,
        ErrorMessage = errorMessage
    };

    /// <summary>
    /// Stop execution and respond with a custom result.
    /// </summary>
    public static HookResult Respond(PluginResult response) => new()
    {
        Continue = false,
        Response = response
    };
}
