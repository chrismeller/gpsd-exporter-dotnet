using System.Diagnostics.Metrics;
using GpsdExporter.Connection;
using GpsdExporter.Protocol;
using C = GpsdExporter.Metrics.MetricCatalogue;

namespace GpsdExporter.Metrics;

/// <summary>
/// The exporter's own health metrics (R5): <c>gpsd_up</c>, build information, and the
/// connection, message, parse-error and scrape figures from <see cref="ConnectionStats"/>.
/// They are on their own meter, separate from the gpsd-derived series, so they can also go
/// to OTLP. Every counter reports every known label value from startup, starting at 0 (AC5.3).
/// </summary>
internal sealed class ExporterMetrics : IDisposable
{
    public const string MeterName = "GpsdExporter";

    /// <summary>Scrape duration buckets, in seconds: 1 ms to 1 s.</summary>
    private static readonly double[] ScrapeDurationBuckets = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1];

    private static readonly KeyValuePair<string, object?>[] BuildInfoTags =
    [
        new("version", BuildInfo.Version),
        new("revision", BuildInfo.Revision),
        new("runtime", BuildInfo.Runtime),
    ];

    private readonly Meter _meter;
    private readonly Histogram<double> _scrapeDuration;

    public ExporterMetrics(IMeterFactory meterFactory, ConnectionStats stats)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(stats);

        _meter = meterFactory.Create(MeterName);

        Gauge(C.Up, () => [new(stats.Up ? 1 : 0)]);
        Gauge(C.BuildInfo, () => [new(1, BuildInfoTags)]);
        Counter(C.ConnectionAttempts, () => PerLabel(Enum.GetValues<ConnectResult>(), "result", r => r.Label(), stats.Attempts));
        Counter(C.Disconnects, () => PerLabel(Enum.GetValues<DisconnectReason>(), "reason", r => r.Label(), stats.Disconnects));
        Gauge(C.ReconnectDelay, () => [new(stats.ReconnectDelay.TotalSeconds)]);
        Gauge(C.ConnectedTimestamp, () => Timestamp(stats.ConnectedAt));
        Counter(C.Messages, () => PerLabel(GpsdClassExtensions.All, "class", c => c.Label(), stats.Messages));
        Counter(C.ParseErrors, () => PerLabel(ParseErrorReasonExtensions.All, "reason", r => r.Label(), stats.ParseErrors));
        Gauge(C.LastMessageTimestamp, () => Timestamp(stats.LastMessageAt));

        _scrapeDuration = _meter.CreateHistogram(
            C.ScrapeDuration.Name,
            unit: null,
            description: C.ScrapeDuration.Help,
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = ScrapeDurationBuckets });
    }

    /// <summary>Records how long a scrape took to collect and write; it shows from the next scrape.</summary>
    public void RecordScrape(TimeSpan duration) => _scrapeDuration.Record(duration.TotalSeconds);

    public void Dispose() => _meter.Dispose();

    private static Measurement<double>[] Timestamp(DateTimeOffset? at) =>
        at is { } t ? [new((t - DateTimeOffset.UnixEpoch).Ticks / (double)TimeSpan.TicksPerSecond)] : [];

    private static IEnumerable<Measurement<long>> PerLabel<T>(IEnumerable<T> values, string label, Func<T, string> name, Func<T, long> count)
    {
        foreach (var value in values)
        {
            yield return new Measurement<long>(count(value), new KeyValuePair<string, object?>(label, name(value)));
        }
    }

    private void Gauge(MetricDefinition metric, Func<IEnumerable<Measurement<double>>> observe) =>
        _meter.CreateObservableGauge(metric.Name, observe, unit: null, description: metric.Help);

    private void Counter(MetricDefinition metric, Func<IEnumerable<Measurement<long>>> observe) =>
        _meter.CreateObservableCounter(metric.Name, observe, unit: null, description: metric.Help);
}
