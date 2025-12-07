using System.Diagnostics.Metrics;

namespace Knutr.Infrastructure.Llm;

/// <summary>
/// Metrics for LLM operations.
/// </summary>
public sealed class LlmMetrics
{
    private readonly Meter _meter;

    /// <summary>Total LLM requests. Tags: provider, model, outcome</summary>
    public Counter<long> Requests { get; }

    /// <summary>LLM response latency in milliseconds. Tags: provider, model</summary>
    public Histogram<double> ResponseLatency { get; }

    /// <summary>LLM input token count. Tags: provider, model</summary>
    public Histogram<long> InputTokens { get; }

    /// <summary>LLM output token count. Tags: provider, model</summary>
    public Histogram<long> OutputTokens { get; }

    /// <summary>LLM errors. Tags: provider, model, error_type</summary>
    public Counter<long> Errors { get; }

    public LlmMetrics()
    {
        _meter = new("Knutr.Infrastructure.Llm");

        Requests = _meter.CreateCounter<long>(
            "knutr_llm_requests_total",
            description: "Total LLM requests by provider and model");

        ResponseLatency = _meter.CreateHistogram<double>(
            "knutr_llm_response_latency_ms",
            unit: "ms",
            description: "LLM response latency in milliseconds");

        InputTokens = _meter.CreateHistogram<long>(
            "knutr_llm_input_tokens",
            unit: "tokens",
            description: "LLM input token count per request");

        OutputTokens = _meter.CreateHistogram<long>(
            "knutr_llm_output_tokens",
            unit: "tokens",
            description: "LLM output token count per request");

        Errors = _meter.CreateCounter<long>(
            "knutr_llm_errors_total",
            description: "Total LLM errors by type");
    }

    public void RecordRequest(string provider, string model, string outcome, double latencyMs)
    {
        Requests.Add(1, [
            new("provider", provider),
            new("model", model),
            new("outcome", outcome)
        ]);

        ResponseLatency.Record(latencyMs, [
            new("provider", provider),
            new("model", model)
        ]);
    }

    public void RecordTokens(string provider, string model, long inputTokens, long outputTokens)
    {
        InputTokens.Record(inputTokens, [
            new("provider", provider),
            new("model", model)
        ]);

        OutputTokens.Record(outputTokens, [
            new("provider", provider),
            new("model", model)
        ]);
    }

    public void RecordError(string provider, string model, string errorType)
    {
        Errors.Add(1, [
            new("provider", provider),
            new("model", model),
            new("error_type", errorType)
        ]);
    }
}
