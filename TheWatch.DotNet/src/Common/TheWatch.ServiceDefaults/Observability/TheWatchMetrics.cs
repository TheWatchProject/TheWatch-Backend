using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TheWatch.ServiceDefaults.Observability;

public static class TheWatchMetrics
{
    public const string MeterName = "TheWatch.Platform";
    private static readonly Meter s_meter = new(MeterName, "2.0.0");

    // Metrics Counters & Histograms
    public static readonly Counter<long> IncidentsCreatedCounter = s_meter.CreateCounter<long>(
        "thewatch.incidents.created.count",
        description: "Total number of emergency incidents reported");

    public static readonly Counter<long> DispatchesCompletedCounter = s_meter.CreateCounter<long>(
        "thewatch.dispatches.completed.count",
        description: "Total number of field responder dispatches executed");

    public static readonly Histogram<double> DispatchDurationHistogram = s_meter.CreateHistogram<double>(
        "thewatch.dispatch.duration.ms",
        unit: "ms",
        description: "Duration in milliseconds from incident report to field unit dispatch");

    // Activity Source for Distributed Tracing
    public static readonly ActivitySource ActivitySource = new("TheWatch.Platform.Tracing", "2.0.0");
}