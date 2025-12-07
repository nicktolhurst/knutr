namespace Knutr.Plugins.GitLabPipeline.Messaging;

/// <summary>
/// Builds elegant deployment status messages using Slack Block Kit.
/// The message is designed to be updated in-place as the deployment progresses.
/// </summary>
public sealed class DeploymentMessageBuilder
{
    private readonly string _branch;
    private readonly string _environment;
    private readonly List<StepStatus> _steps = new();
    private string? _pipelineUrl;
    private DeploymentState _state = DeploymentState.InProgress;
    private string? _errorMessage;
    private TimeSpan? _duration;

    public DeploymentMessageBuilder(string branch, string environment)
    {
        _branch = branch;
        _environment = environment;
    }

    public DeploymentMessageBuilder SetPipelineUrl(string url)
    {
        _pipelineUrl = url;
        return this;
    }

    public DeploymentMessageBuilder SetDuration(TimeSpan duration)
    {
        _duration = duration;
        return this;
    }

    public DeploymentMessageBuilder AddStep(string name, StepState state, string? detail = null)
    {
        var existing = _steps.FindIndex(s => s.Name == name);
        if (existing >= 0)
        {
            _steps[existing] = new StepStatus(name, state, detail);
        }
        else
        {
            _steps.Add(new StepStatus(name, state, detail));
        }
        return this;
    }

    public DeploymentMessageBuilder MarkSuccess()
    {
        _state = DeploymentState.Success;
        return this;
    }

    public DeploymentMessageBuilder MarkFailed(string? error = null)
    {
        _state = DeploymentState.Failed;
        _errorMessage = error;
        return this;
    }

    public DeploymentMessageBuilder MarkCancelled()
    {
        _state = DeploymentState.Cancelled;
        return this;
    }

    /// <summary>Builds the fallback text for notifications.</summary>
    public string BuildText()
    {
        return _state switch
        {
            DeploymentState.Success => $"✅ Deployed {_branch} to {_environment}",
            DeploymentState.Failed => $"❌ Deployment failed: {_branch} to {_environment}",
            DeploymentState.Cancelled => $"⏹️ Deployment cancelled: {_branch} to {_environment}",
            _ => $"🚀 Deploying {_branch} to {_environment}..."
        };
    }

    /// <summary>Builds the Block Kit blocks array.</summary>
    public object[] BuildBlocks()
    {
        var blocks = new List<object>();

        // Header section
        var headerEmoji = _state switch
        {
            DeploymentState.Success => "✅",
            DeploymentState.Failed => "❌",
            DeploymentState.Cancelled => "⏹️",
            _ => "🚀"
        };

        var headerText = _state switch
        {
            DeploymentState.Success => "Deployment Complete",
            DeploymentState.Failed => "Deployment Failed",
            DeploymentState.Cancelled => "Deployment Cancelled",
            _ => "Deploying..."
        };

        blocks.Add(SlackBlocks.Section($"{headerEmoji}  *{headerText}*"));

        // Steps section
        if (_steps.Count > 0)
        {
            var stepsText = string.Join("\n", _steps.Select(FormatStep));
            blocks.Add(SlackBlocks.Section(stepsText));
        }

        // Error message if failed
        if (_state == DeploymentState.Failed && !string.IsNullOrEmpty(_errorMessage))
        {
            blocks.Add(SlackBlocks.Section($"⚠️  _{_errorMessage}_"));
        }

        // Context footer with metadata
        var contextParts = new List<string>
        {
            $"`{_branch}` → `{_environment}`"
        };

        if (_duration.HasValue)
        {
            contextParts.Add(FormatDuration(_duration.Value));
        }

        if (!string.IsNullOrEmpty(_pipelineUrl))
        {
            contextParts.Add($"<{_pipelineUrl}|View Pipeline>");
        }

        blocks.Add(SlackBlocks.Context(string.Join("  •  ", contextParts)));

        return blocks.ToArray();
    }

    private static string FormatStep(StepStatus step)
    {
        var icon = step.State switch
        {
            StepState.Pending => "○",
            StepState.InProgress => "◐",
            StepState.Success => "●",
            StepState.Failed => "✗",
            StepState.Skipped => "◌",
            _ => "○"
        };

        var text = step.Detail is not null
            ? $"{step.Name}  `{step.Detail}`"
            : step.Name;

        return $"    {icon}  {text}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
            return $"{duration.Seconds}s";
        if (duration.TotalMinutes < 60)
            return $"{duration.Minutes}m {duration.Seconds}s";
        return $"{duration.Hours}h {duration.Minutes}m";
    }

    private record StepStatus(string Name, StepState State, string? Detail);
}

public enum DeploymentState
{
    InProgress,
    Success,
    Failed,
    Cancelled
}

public enum StepState
{
    Pending,
    InProgress,
    Success,
    Failed,
    Skipped
}
