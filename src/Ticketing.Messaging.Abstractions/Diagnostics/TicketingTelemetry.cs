using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ticketing.Messaging.Abstractions.Diagnostics;

/// <summary>
/// Shared telemetry constants for distributed tracing and metrics across all agents.
/// </summary>
public static class TicketingTelemetry
{
    public const string SourceName = "Ticketing.Agents";

    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(SourceName);

    // Message processing metrics
    public static readonly Counter<long> EventsPublished =
        Meter.CreateCounter<long>("messaging.events.published", description: "Number of events published");

    public static readonly Counter<long> EventsProcessed =
        Meter.CreateCounter<long>("messaging.events.processed", description: "Number of events processed");

    public static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("messaging.processing.duration_ms", "ms", "Message processing duration");

    // LLM metrics
    public static readonly Counter<long> LlmCalls =
        Meter.CreateCounter<long>("llm.calls", description: "Number of LLM calls");

    public static readonly Histogram<double> LlmDuration =
        Meter.CreateHistogram<double>("llm.duration_ms", "ms", "LLM call duration");

    public static readonly Counter<long> LlmTokens =
        Meter.CreateCounter<long>("llm.tokens", description: "LLM token usage");
}
