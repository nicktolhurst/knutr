using System.Diagnostics.Metrics;

namespace Knutr.Core.Observability;

public sealed class CoreMetrics
{
    private readonly Meter _meter;
    public Counter<long> Messages { get; }
    public Counter<long> CommandsMatched { get; }
    public Counter<long> Replies { get; }
    public Histogram<double> OrchestratorLatency { get; }

    public CoreMetrics()
    {
        _meter = new("Knutr.Core");
        Messages = _meter.CreateCounter<long>("knutr_messages_total");
        CommandsMatched = _meter.CreateCounter<long>("knutr_commands_matched_total");
        Replies = _meter.CreateCounter<long>("knutr_replies_total");
        OrchestratorLatency = _meter.CreateHistogram<double>("knutr_orchestrator_latency_ms");
    }
}
