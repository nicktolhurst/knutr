using System.Diagnostics.Metrics;

namespace Knutr.Core.Observability;

/// <summary>
/// Core metrics for Knutr bot operations.
/// Dimensions:
/// - type: command, message
/// - channel: channel ID
/// - command: command name (for commands)
/// - subcommand: subcommand name (for /knutr subcommands)
/// </summary>
public sealed class CoreMetrics
{
    private readonly Meter _meter;

    /// <summary>Total messages received. Tags: type (command/message), channel</summary>
    public Counter<long> Messages { get; }

    /// <summary>Total commands matched. Tags: command, subcommand, channel</summary>
    public Counter<long> CommandsMatched { get; }

    /// <summary>Total subcommand invocations. Tags: subcommand, channel, user</summary>
    public Counter<long> SubcommandInvocations { get; }

    /// <summary>Total replies sent. Tags: channel, type (ephemeral/in_channel/thread/dm)</summary>
    public Counter<long> Replies { get; }

    /// <summary>Orchestrator latency in milliseconds. Tags: type (command/message), outcome (success/error)</summary>
    public Histogram<double> OrchestratorLatency { get; }

    /// <summary>Intent recognition latency in milliseconds. Tags: recognized (true/false)</summary>
    public Histogram<double> IntentRecognitionLatency { get; }

    /// <summary>Errors encountered during processing. Tags: type, source, error_type</summary>
    public Counter<long> Errors { get; }

    /// <summary>Active workflows in progress. Tags: workflow_type</summary>
    public UpDownCounter<long> ActiveWorkflows { get; }

    public CoreMetrics()
    {
        _meter = new("Knutr.Core");

        Messages = _meter.CreateCounter<long>(
            "knutr_messages_total",
            description: "Total messages received by type and channel");

        CommandsMatched = _meter.CreateCounter<long>(
            "knutr_commands_matched_total",
            description: "Total commands matched by command and subcommand");

        SubcommandInvocations = _meter.CreateCounter<long>(
            "knutr_subcommand_invocations_total",
            description: "Total subcommand invocations by subcommand and channel");

        Replies = _meter.CreateCounter<long>(
            "knutr_replies_total",
            description: "Total replies sent by channel and type");

        OrchestratorLatency = _meter.CreateHistogram<double>(
            "knutr_orchestrator_latency_ms",
            unit: "ms",
            description: "Orchestrator processing latency in milliseconds");

        IntentRecognitionLatency = _meter.CreateHistogram<double>(
            "knutr_intent_recognition_latency_ms",
            unit: "ms",
            description: "Intent recognition latency in milliseconds");

        Errors = _meter.CreateCounter<long>(
            "knutr_errors_total",
            description: "Total errors by type and source");

        ActiveWorkflows = _meter.CreateUpDownCounter<long>(
            "knutr_active_workflows",
            description: "Currently active workflows by type");
    }

    public void RecordMessage(string type, string channelId)
    {
        Messages.Add(1, [
            new("type", type),
            new("channel", channelId)
        ]);
    }

    public void RecordCommandMatched(string command, string? subcommand, string channelId)
    {
        CommandsMatched.Add(1, [
            new("command", command),
            new("subcommand", subcommand ?? "none"),
            new("channel", channelId)
        ]);
    }

    public void RecordSubcommandInvocation(string subcommand, string channelId, string userId)
    {
        SubcommandInvocations.Add(1, [
            new("subcommand", subcommand),
            new("channel", channelId),
            new("user", userId)
        ]);
    }

    public void RecordReply(string channelId, string replyType)
    {
        Replies.Add(1, [
            new("channel", channelId),
            new("type", replyType)
        ]);
    }

    public void RecordLatency(double milliseconds, string type, string outcome)
    {
        OrchestratorLatency.Record(milliseconds, [
            new("type", type),
            new("outcome", outcome)
        ]);
    }

    public void RecordIntentLatency(double milliseconds, bool recognized)
    {
        IntentRecognitionLatency.Record(milliseconds, [
            new("recognized", recognized.ToString().ToLowerInvariant())
        ]);
    }

    public void RecordError(string type, string source, string errorType)
    {
        Errors.Add(1, [
            new("type", type),
            new("source", source),
            new("error_type", errorType)
        ]);
    }

    public void WorkflowStarted(string workflowType)
    {
        ActiveWorkflows.Add(1, [new("workflow_type", workflowType)]);
    }

    public void WorkflowCompleted(string workflowType)
    {
        ActiveWorkflows.Add(-1, [new("workflow_type", workflowType)]);
    }
}
