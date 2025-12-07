namespace Knutr.Core.Hooks;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Knutr.Abstractions.Hooks;
using Microsoft.Extensions.Logging;

/// <summary>
/// Registry for plugin hooks with pattern matching and priority-based execution.
/// </summary>
public sealed class HookRegistry : IHookRegistry
{
    private readonly ConcurrentDictionary<HookPoint, List<HookRegistration>> _hooks = new();
    private readonly ILogger<HookRegistry> _log;

    public HookRegistry(ILogger<HookRegistry> log)
    {
        _log = log;
        foreach (var point in Enum.GetValues<HookPoint>())
        {
            _hooks[point] = [];
        }
    }

    public IHookBuilder On(HookPoint point, string pattern, HookHandler handler, int priority = 0)
    {
        var registration = new HookRegistration(pattern, CompilePattern(pattern), handler, priority);

        lock (_hooks[point])
        {
            _hooks[point].Add(registration);
            _hooks[point].Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        _log.LogDebug("Registered {Point} hook for pattern '{Pattern}' with priority {Priority}",
            point, pattern, priority);

        return this;
    }

    public async Task<HookResult> ExecuteAsync(HookPoint point, HookContext context, CancellationToken ct = default)
    {
        var commandKey = BuildCommandKey(context);
        var matchingHooks = GetMatchingHooks(point, commandKey);

        if (matchingHooks.Count == 0)
        {
            _log.LogDebug("No {Point} hooks matched for '{CommandKey}'", point, commandKey);
            return HookResult.Ok();
        }

        _log.LogDebug("Executing {Count} {Point} hooks for '{CommandKey}'",
            matchingHooks.Count, point, commandKey);

        foreach (var hook in matchingHooks)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await hook.Handler(context, ct);

                if (!result.Continue)
                {
                    _log.LogInformation("Hook '{Pattern}' at {Point} stopped pipeline: {Reason}",
                        hook.Pattern, point, result.ErrorMessage ?? "custom response");
                    return result;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Hook '{Pattern}' at {Point} threw exception", hook.Pattern, point);

                // For OnError hooks, we don't want to throw again
                if (point == HookPoint.OnError)
                    continue;

                throw;
            }
        }

        return HookResult.Ok();
    }

    public int CountHooks(HookPoint point) => _hooks[point].Count;

    private List<HookRegistration> GetMatchingHooks(HookPoint point, string commandKey)
    {
        lock (_hooks[point])
        {
            return _hooks[point]
                .Where(h => h.CompiledPattern.IsMatch(commandKey))
                .ToList();
        }
    }

    private static string BuildCommandKey(HookContext context)
    {
        // Build key in format: "command:action:arg1:arg2..."
        // e.g., "knutr:deploy:feature/new-feature:demo"
        var parts = new List<string> { context.Command };

        if (!string.IsNullOrEmpty(context.Action))
            parts.Add(context.Action);

        // Add specific arguments that are commonly used for matching
        if (context.Arguments.TryGetValue("environment", out var env) && env is string envStr)
            parts.Add(envStr);
        else if (context.Arguments.TryGetValue("ref", out var refVal) && refVal is string refStr)
            parts.Add(refStr);

        return string.Join(":", parts).ToLowerInvariant();
    }

    private static Regex CompilePattern(string pattern)
    {
        // Convert glob-like pattern to regex
        // * matches any single segment
        // ** matches any number of segments
        var escaped = Regex.Escape(pattern.ToLowerInvariant());
        var regexPattern = escaped
            .Replace(@"\*\*", ".*")     // ** = match anything
            .Replace(@"\*", "[^:]*");   // * = match within segment

        return new Regex($"^{regexPattern}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    private sealed record HookRegistration(
        string Pattern,
        Regex CompiledPattern,
        HookHandler Handler,
        int Priority);
}
