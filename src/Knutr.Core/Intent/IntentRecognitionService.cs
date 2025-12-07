namespace Knutr.Core.Intent;

using System.Text.RegularExpressions;
using Knutr.Abstractions.Intent;
using Microsoft.Extensions.Logging;

/// <summary>
/// Pattern-based intent recognition service for common CI/CD commands.
/// </summary>
public sealed class IntentRecognitionService(ILogger<IntentRecognitionService> log) : IIntentRecognizer
{
    // Deploy patterns: "deploy main to prod", "deploy the feature branch to demo", "deploy main branch to staging"
    private static readonly Regex DeployPattern = new(
        @"(?:please\s+)?deploy(?:\s+the)?\s+(?<branch>[\w\-\/\.]+)(?:\s+branch)?\s+(?:to|on|in)\s+(?<env>[\w\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Deploy without environment: "deploy main", "deploy feature/xyz"
    private static readonly Regex DeploySimplePattern = new(
        @"(?:please\s+)?deploy(?:\s+the)?\s+(?<branch>[\w\-\/\.]+)(?:\s+branch)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Build patterns: "build main", "build the feature branch", "run build on develop"
    private static readonly Regex BuildPattern = new(
        @"(?:please\s+)?(?:build|run\s+build(?:\s+on)?)(?:\s+the)?\s+(?<branch>[\w\-\/\.]+)(?:\s+branch)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Status patterns: "status", "pipeline status", "check status", "show pipelines"
    private static readonly Regex StatusPattern = new(
        @"(?:show\s+)?(?:pipeline\s+)?status|show\s+pipelines?|check\s+(?:pipeline\s+)?status",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cancel patterns: "cancel pipeline 123", "cancel build", "stop pipeline"
    private static readonly Regex CancelPattern = new(
        @"(?:please\s+)?(?:cancel|stop|abort)(?:\s+(?:the\s+)?(?:pipeline|build))?(?:\s+(?<id>\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Retry patterns: "retry pipeline 123", "retry the last build"
    private static readonly Regex RetryPattern = new(
        @"(?:please\s+)?retry(?:\s+(?:the\s+)?(?:last\s+)?(?:pipeline|build))?(?:\s+(?<id>\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<IntentResult> RecognizeAsync(string text, CancellationToken ct = default)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(IntentResult.None);

        // Try deploy with environment first (most specific)
        var deployMatch = DeployPattern.Match(text);
        if (deployMatch.Success)
        {
            var branch = deployMatch.Groups["branch"].Value;
            var env = deployMatch.Groups["env"].Value;
            log.LogDebug("Recognized deploy intent: branch={Branch}, env={Env}", branch, env);
            return Task.FromResult(IntentResult.Deploy(branch, env, 0.9f));
        }

        // Try simple deploy
        var deploySimpleMatch = DeploySimplePattern.Match(text);
        if (deploySimpleMatch.Success)
        {
            var branch = deploySimpleMatch.Groups["branch"].Value;
            log.LogDebug("Recognized deploy intent: branch={Branch}", branch);
            return Task.FromResult(IntentResult.Deploy(branch, null, 0.85f));
        }

        // Try build
        var buildMatch = BuildPattern.Match(text);
        if (buildMatch.Success)
        {
            var branch = buildMatch.Groups["branch"].Value;
            log.LogDebug("Recognized build intent: branch={Branch}", branch);
            return Task.FromResult(IntentResult.Build(branch, 0.9f));
        }

        // Try status
        if (StatusPattern.IsMatch(text))
        {
            log.LogDebug("Recognized status intent");
            return Task.FromResult(IntentResult.Status(0.9f));
        }

        // Try cancel
        var cancelMatch = CancelPattern.Match(text);
        if (cancelMatch.Success)
        {
            var id = cancelMatch.Groups["id"].Success ? cancelMatch.Groups["id"].Value : null;
            log.LogDebug("Recognized cancel intent: id={Id}", id ?? "latest");
            return Task.FromResult(new IntentResult("gitlab", "cancel", new Dictionary<string, string>
            {
                ["id"] = id ?? ""
            }, 0.85f));
        }

        // Try retry
        var retryMatch = RetryPattern.Match(text);
        if (retryMatch.Success)
        {
            var id = retryMatch.Groups["id"].Success ? retryMatch.Groups["id"].Value : null;
            log.LogDebug("Recognized retry intent: id={Id}", id ?? "latest");
            return Task.FromResult(new IntentResult("gitlab", "retry", new Dictionary<string, string>
            {
                ["id"] = id ?? ""
            }, 0.85f));
        }

        log.LogDebug("No intent recognized from: {Text}", text);
        return Task.FromResult(IntentResult.None);
    }
}
