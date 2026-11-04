using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CompanyEnrichment.Observability;

/// <summary>
/// Records bounded-cardinality metrics for logical downstream dependency calls (not raw retry
/// attempts — resilience retries are an implementation detail of the HttpClient pipeline and are
/// observed separately via OpenTelemetry's HTTP client instrumentation).
/// Labels are restricted to: dependency name and outcome (success/not_found/error/timeout) —
/// never CVR numbers, company names, or raw URLs.
/// This type has no dependency on any specific monitoring vendor; the consuming application
/// decides how the underlying <see cref="Meter"/> is collected and exported.
/// </summary>
public sealed class DependencyMetrics : IDisposable
{
    public const string MeterName = "CompanyEnrichment.Dependencies";

    private readonly Meter _meter;
    private readonly Counter<long> _requestCounter;
    private readonly Histogram<double> _durationHistogram;

    public DependencyMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _requestCounter = _meter.CreateCounter<long>(
            "dependency_requests_total", unit: "{request}", description: "Total logical downstream dependency calls.");
        _durationHistogram = _meter.CreateHistogram<double>(
            "dependency_request_duration_seconds", unit: "s", description: "Duration of logical downstream dependency calls.");
    }

    public DependencyCallScope StartCall(string dependency) => new(this, dependency);

    internal void Record(string dependency, string outcome, double elapsedSeconds)
    {
        var tags = new TagList
        {
            { "dependency", dependency },
            { "outcome", outcome },
        };

        _requestCounter.Add(1, tags);
        _durationHistogram.Record(elapsedSeconds, tags);
    }

    public void Dispose() => _meter.Dispose();
}

/// <summary>Scope that measures and records the outcome of a single logical dependency call.</summary>
public readonly struct DependencyCallScope : IDisposable
{
    private readonly DependencyMetrics _metrics;
    private readonly string _dependency;
    private readonly Stopwatch _stopwatch;

    internal DependencyCallScope(DependencyMetrics metrics, string dependency)
    {
        _metrics = metrics;
        _dependency = dependency;
        _stopwatch = Stopwatch.StartNew();
    }

    public void Success() => Complete("success");

    public void NotFound() => Complete("not_found");

    public void Error() => Complete("error");

    public void Timeout() => Complete("timeout");

    private void Complete(string outcome) =>
        _metrics.Record(_dependency, outcome, _stopwatch.Elapsed.TotalSeconds);

    public void Dispose()
    {
        // No-op: callers must explicitly report an outcome via Success/NotFound/Error/Timeout.
    }
}
