using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Globalization;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using C = GpsdExporter.Metrics.MetricCatalogue;

namespace GpsdExporter.Metrics;

/// <summary>
/// The gpsd-derived instruments: TPV, SKY, satellites, PPS, TOFF and OSC. Each one is an
/// observable instrument whose callback reads the scrape's <see cref="StateSnapshot"/> and
/// reports only the values that are present and fresh (R3, R4). A value that is absent is
/// simply not reported, so its series disappears from the next scrape.
/// </summary>
internal sealed class GpsdMetrics : IDisposable
{
    /// <summary>The meter for gpsd-derived metrics. It is exposed on <c>/metrics</c> only, never sent over OTLP.</summary>
    public const string MeterName = "GpsdExporter.Gpsd";

    private const double NanosecondsPerSecond = 1e9;

    /// <summary>The per-satellite instruments, which get the higher cardinality limit (AC6.7).</summary>
    public static ImmutableArray<MetricDefinition> SatelliteMetrics { get; } =
    [
        C.SatelliteUsed, C.SatelliteSnr, C.SatelliteAzimuth, C.SatelliteElevation, C.SatelliteHealth, C.SatelliteQuality,
        C.SatellitePseudorangeResidual,
    ];

    /// <summary>How each TPV field becomes a series: its metric, an optional fixed label and the divisor that converts it to base units.</summary>
    private static readonly ImmutableArray<FieldSeries<TpvField>> TpvSeries =
    [
        new(TpvField.Status, C.TpvStatus),
        new(TpvField.LeapSeconds, C.TpvLeapSeconds),
        new(TpvField.Lat, C.TpvLatitude),
        new(TpvField.Lon, C.TpvLongitude),
        new(TpvField.AltHae, C.TpvAltitudeHae),
        new(TpvField.AltMsl, C.TpvAltitudeMsl),
        new(TpvField.GeoidSep, C.TpvGeoidSeparation),
        new(TpvField.Speed, C.TpvSpeed),
        new(TpvField.Climb, C.TpvClimb),
        new(TpvField.Track, C.TpvTrack),
        new(TpvField.MagTrack, C.TpvMagneticTrack),
        new(TpvField.MagVar, C.TpvMagneticVariation),
        new(TpvField.VelN, C.TpvVelocity, "axis", "north"),
        new(TpvField.VelE, C.TpvVelocity, "axis", "east"),
        new(TpvField.VelD, C.TpvVelocity, "axis", "down"),
        new(TpvField.EcefX, C.TpvEcefPosition, "axis", "x"),
        new(TpvField.EcefY, C.TpvEcefPosition, "axis", "y"),
        new(TpvField.EcefZ, C.TpvEcefPosition, "axis", "z"),
        new(TpvField.EcefVx, C.TpvEcefVelocity, "axis", "x"),
        new(TpvField.EcefVy, C.TpvEcefVelocity, "axis", "y"),
        new(TpvField.EcefVz, C.TpvEcefVelocity, "axis", "z"),
        new(TpvField.EcefPAcc, C.TpvEcefPositionError),
        new(TpvField.EcefVAcc, C.TpvEcefVelocityError),
        new(TpvField.RelN, C.TpvRelativePosition, "axis", "north"),
        new(TpvField.RelE, C.TpvRelativePosition, "axis", "east"),
        new(TpvField.RelD, C.TpvRelativePosition, "axis", "down"),
        new(TpvField.Eph, C.TpvPositionError, "kind", "horizontal"),
        new(TpvField.Epv, C.TpvPositionError, "kind", "vertical"),
        new(TpvField.Epy, C.TpvPositionError, "kind", "latitude"),
        new(TpvField.Epx, C.TpvPositionError, "kind", "longitude"),
        new(TpvField.Sep, C.TpvPositionError, "kind", "spherical"),
        new(TpvField.Ept, C.TpvTimeError),
        new(TpvField.Eps, C.TpvSpeedError),
        new(TpvField.Epc, C.TpvClimbError),
        new(TpvField.Epd, C.TpvTrackError),
        new(TpvField.ClockBias, C.TpvClockBias, Divisor: NanosecondsPerSecond),
        new(TpvField.ClockDrift, C.TpvClockDrift, Divisor: NanosecondsPerSecond),
        new(TpvField.DgpsAge, C.TpvDgpsAge),
        new(TpvField.DgpsSta, C.TpvDgpsStation),
        new(TpvField.Jam, C.TpvJamming),
        new(TpvField.Ant, C.TpvAntenna),
        new(TpvField.AntPwr, C.TpvAntennaPower),
        new(TpvField.Temp, C.TpvTemperature),
    ];

    private static readonly ImmutableArray<FieldSeries<SkyField>> SkySeries =
    [
        new(SkyField.Gdop, C.SkyDop, "kind", "geometric"),
        new(SkyField.Hdop, C.SkyDop, "kind", "horizontal"),
        new(SkyField.Pdop, C.SkyDop, "kind", "position"),
        new(SkyField.Tdop, C.SkyDop, "kind", "time"),
        new(SkyField.Vdop, C.SkyDop, "kind", "vertical"),
        new(SkyField.Xdop, C.SkyDop, "kind", "longitudinal"),
        new(SkyField.Ydop, C.SkyDop, "kind", "latitudinal"),
    ];

    private readonly Meter _meter;
    private readonly ScrapeSnapshot _scrape;

    public GpsdMetrics(IMeterFactory meterFactory, ScrapeSnapshot scrape, MetricsOptions options)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(scrape);
        ArgumentNullException.ThrowIfNull(options);

        _meter = meterFactory.Create(MeterName);
        _scrape = scrape;

        // TPV.
        Gauge(C.TpvMode, s => PerDevice(s, d => d.TpvMode));
        Gauge(C.TpvTimestamp, s => PerDevice(s, d => UnixSeconds(d.TpvTimestamp)));
        foreach (var group in TpvSeries.GroupBy(f => f.Metric))
        {
            var series = group.ToImmutableArray();
            Gauge(group.Key, s => Fields(s, series, static (d, f) => d.Tpv(f)));
        }

        // SKY.
        Gauge(C.SkySatellitesVisible, s => PerDevice(s, d => d.SatellitesVisible));
        Gauge(C.SkySatellitesUsed, s => PerDevice(s, d => d.SatellitesUsed));
        Gauge(C.SkyDop, s => Fields(s, SkySeries, static (d, f) => d.Sky(f)));

        if (options.Satellites)
        {
            Gauge(C.SatelliteUsed, s => PerSatellite(s, sat => sat.Used is { } used ? (used ? 1 : 0) : null));
            Gauge(C.SatelliteSnr, s => PerSatellite(s, sat => sat.Fields[SatelliteField.Ss]));
            Gauge(C.SatelliteAzimuth, s => PerSatellite(s, sat => sat.Fields[SatelliteField.Az]));
            Gauge(C.SatelliteElevation, s => PerSatellite(s, sat => sat.Fields[SatelliteField.El]));
            Gauge(C.SatelliteHealth, s => PerSatellite(s, sat => sat.Fields[SatelliteField.Health]));
            Gauge(C.SatelliteQuality, s => PerSatellite(s, sat => sat.Fields[SatelliteField.Qual]));
            Gauge(C.SatellitePseudorangeResidual, s => PerSatellite(s, sat => sat.Fields[SatelliteField.PrRes]));
        }

        // PPS, TOFF and OSC.
        Gauge(C.PpsOffset, s => PerDevice(s, d => d.Pps?.OffsetSeconds));
        Gauge(C.PpsPrecision, s => PerDevice(s, d => d.Pps?.PrecisionLog2Seconds));
        Gauge(C.PpsQuantizationError, s => PerDevice(s, d => d.Pps?.QuantizationErrorSeconds));
        Gauge(C.PpsPulseTimestamp, s => PerDevice(s, d => d.Pps?.PulseTimestampSeconds));
        _meter.CreateObservableCounter(C.PpsPulses.Name, () => PpsPulses(_scrape.Current), unit: null, description: C.PpsPulses.Help);
        Gauge(C.ToffOffset, s => PerDevice(s, d => d.Toff?.OffsetSeconds));
        Gauge(C.ToffPrecision, s => PerDevice(s, d => d.Toff?.PrecisionLog2Seconds));
        Gauge(C.OscRunning, s => PerDevice(s, d => Flag(d.Osc?.Running)));
        Gauge(C.OscReference, s => PerDevice(s, d => Flag(d.Osc?.Reference)));
        Gauge(C.OscDisciplined, s => PerDevice(s, d => Flag(d.Osc?.Disciplined)));
        Gauge(C.OscDelta, s => PerDevice(s, d => d.Osc?.DeltaSeconds));
    }

    public void Dispose() => _meter.Dispose();

    /// <summary>The label set of a satellite signal: only the identity labels gpsd supplied (AC6.3).</summary>
    internal static KeyValuePair<string, object?>[] SatelliteTags(string device, Satellite satellite)
    {
        var tags = new List<KeyValuePair<string, object?>>(6) { new("device", device) };
        if (satellite is { GnssId: { } gnss, SvId: { } sv })
        {
            tags.Add(new("constellation", Constellation.FromGnssId(gnss)));
            tags.Add(new("gnssid", Label(gnss)));
            tags.Add(new("svid", Label(sv)));
            if (satellite.SigId is { } sig)
            {
                tags.Add(new("sigid", Label(sig)));
            }
        }

        tags.Add(new("prn", Label(satellite.Prn)));
        return [.. tags];
    }

    private static double? UnixSeconds(DateTimeOffset? time) =>
        time is { } t ? (t - DateTimeOffset.UnixEpoch).Ticks / (double)TimeSpan.TicksPerSecond : null;

    private static string Label(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static double? Flag(bool? value) => value is { } v ? (v ? 1 : 0) : null;

    private static KeyValuePair<string, object?> DeviceTag(DeviceSnapshot device) => new("device", device.Path);

    private static IEnumerable<Measurement<double>> PerDevice(StateSnapshot snapshot, Func<DeviceSnapshot, double?> value)
    {
        foreach (var device in snapshot.Devices)
        {
            if (value(device) is { } v)
            {
                yield return new Measurement<double>(v, DeviceTag(device));
            }
        }
    }

    private static IEnumerable<Measurement<double>> Fields<TField>(
        StateSnapshot snapshot,
        ImmutableArray<FieldSeries<TField>> series,
        Func<DeviceSnapshot, TField, double?> read)
        where TField : struct, Enum
    {
        foreach (var device in snapshot.Devices)
        {
            foreach (var s in series)
            {
                if (read(device, s.Field) is not { } v)
                {
                    continue;
                }

                yield return s.LabelName is null
                    ? new Measurement<double>(v / s.Divisor, DeviceTag(device))
                    : new Measurement<double>(v / s.Divisor, DeviceTag(device), new(s.LabelName, s.LabelValue));
            }
        }
    }

    private static IEnumerable<Measurement<double>> PerSatellite(StateSnapshot snapshot, Func<Satellite, double?> value)
    {
        foreach (var device in snapshot.Devices)
        {
            foreach (var satellite in device.Satellites)
            {
                if (value(satellite) is { } v)
                {
                    yield return new Measurement<double>(v, SatelliteTags(device.Path, satellite));
                }
            }
        }
    }

    private static IEnumerable<Measurement<long>> PpsPulses(StateSnapshot snapshot)
    {
        foreach (var device in snapshot.Devices)
        {
            if (device.PpsPulses is { } pulses)
            {
                yield return new Measurement<long>(pulses, DeviceTag(device));
            }
        }
    }

    private void Gauge(MetricDefinition metric, Func<StateSnapshot, IEnumerable<Measurement<double>>> observe) =>
        _meter.CreateObservableGauge(metric.Name, () => observe(_scrape.Current), unit: null, description: metric.Help);

    private sealed record FieldSeries<TField>(TField Field, MetricDefinition Metric, string? LabelName = null, string? LabelValue = null, double Divisor = 1)
        where TField : struct, Enum;
}
