using System.Diagnostics.Metrics;
using GpsdExporter.Connection;
using GpsdExporter.Protocol;
using OpenTelemetry.Metrics;
using C = GpsdExporter.Metrics.MetricCatalogue;

namespace GpsdExporter.Telemetry;

/// <summary>
/// The exporter's own health metrics for OTLP: <c>gpsd_up</c>, build information, and the
/// connection, message and parse-error figures from <see cref="ConnectionStats"/>. Nothing
/// gpsd reports is on this meter; gpsd data is only ever served on <c>/metrics</c>.
/// </summary>
/// <remarks>
/// The names are the catalogue's, with OpenTelemetry units, and without <c>_total</c> on
/// counters: OpenTelemetry-to-Prometheus translation adds <c>_total</c> to counters and drops
/// <c>{annotation}</c> units, so a backend that translates them shows the names that
/// <c>/metrics</c> uses.
/// </remarks>
internal sealed class ExporterMeter
{
    public const string Name = "GpsdExporter";

    private static readonly KeyValuePair<string, object?>[] BuildInfoTags =
    [
        new("version", BuildInfo.Version),
        new("revision", BuildInfo.Revision),
        new("runtime", BuildInfo.Runtime),
    ];

    public ExporterMeter(IMeterFactory meterFactory, ConnectionStats stats)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(stats);

        // The factory owns the meter and disposes it with the service provider.
        var meter = meterFactory.Create(Name);

        meter.CreateObservableGauge(C.Up.Name, () => stats.Up ? 1 : 0, unit: null, C.Up.Help);
        meter.CreateObservableGauge(C.BuildInfo.Name, () => new Measurement<int>(1, BuildInfoTags), unit: null, C.BuildInfo.Help);
        meter.CreateObservableCounter(
            CounterName(C.ConnectionAttempts.Name),
            () => PerLabel(Enum.GetValues<ConnectResult>(), "result", r => r.Label(), stats.Attempts),
            unit: "{attempt}",
            C.ConnectionAttempts.Help);
        meter.CreateObservableCounter(
            CounterName(C.Disconnects.Name),
            () => PerLabel(Enum.GetValues<DisconnectReason>(), "reason", r => r.Label(), stats.Disconnects),
            unit: "{disconnect}",
            C.Disconnects.Help);
        meter.CreateObservableCounter(
            CounterName(C.Messages.Name),
            () => PerLabel(GpsdClassExtensions.All, "class", c => c.Label(), stats.Messages),
            unit: "{message}",
            C.Messages.Help);
        meter.CreateObservableCounter(
            CounterName(C.ParseErrors.Name),
            () => PerLabel(ParseErrorReasonExtensions.All, "reason", r => r.Label(), stats.ParseErrors),
            unit: "{error}",
            C.ParseErrors.Help);
        meter.CreateObservableGauge(C.ReconnectDelay.Name, () => stats.ReconnectDelay.TotalSeconds, unit: "s", C.ReconnectDelay.Help);
        meter.CreateObservableGauge(C.ConnectedTimestamp.Name, () => Timestamp(stats.ConnectedAt), unit: "s", C.ConnectedTimestamp.Help);
        meter.CreateObservableGauge(C.LastMessageTimestamp.Name, () => Timestamp(stats.LastMessageAt), unit: "s", C.LastMessageTimestamp.Help);
    }

    private static string CounterName(string catalogueName) =>
        catalogueName.EndsWith("_total", StringComparison.Ordinal) ? catalogueName[..^"_total".Length] : catalogueName;

    private static Measurement<double>[] Timestamp(DateTimeOffset? at) =>
        at is { } t ? [new((t - DateTimeOffset.UnixEpoch).TotalSeconds)] : [];

    private static IEnumerable<Measurement<long>> PerLabel<T>(IEnumerable<T> values, string label, Func<T, string> name, Func<T, long> count)
    {
        foreach (var value in values)
        {
            yield return new Measurement<long>(count(value), new KeyValuePair<string, object?>(label, name(value)));
        }
    }
}

internal static class ExporterMeterExtensions
{
    /// <summary>
    /// Puts the exporter's own meter on the OpenTelemetry meter provider. The provider creates
    /// it when it starts, so the instruments exist even though nothing else resolves it.
    /// </summary>
    public static TBuilder AddExporterMeter<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics
            .AddMeter(ExporterMeter.Name)
            .AddInstrumentation(services => new ExporterMeter(
                services.GetRequiredService<IMeterFactory>(),
                services.GetRequiredService<ConnectionStats>())));
        return builder;
    }
}
