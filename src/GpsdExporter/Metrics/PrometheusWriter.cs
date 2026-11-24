using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using GpsdExporter.Connection;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using C = GpsdExporter.Metrics.MetricCatalogue;

namespace GpsdExporter.Metrics;

/// <summary>
/// Writes one scrape in the Prometheus text exposition format (version 0.0.4): every metric
/// in <see cref="MetricCatalogue"/>, the gpsd series from one <see cref="StateSnapshot"/> and
/// the exporter's own from <see cref="ConnectionStats"/>. A gpsd value that is absent or stale
/// is not written, so its series disappears; a family with no series is left out entirely,
/// HELP and TYPE included. No sample carries a timestamp.
/// </summary>
internal sealed class PrometheusWriter
{
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    private const double NanosecondsPerSecond = 1e9;

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

    private readonly StringBuilder _text = new();
    private readonly StateSnapshot _snapshot;
    private MetricDefinition? _family;
    private bool _headerWritten;

    private PrometheusWriter(StateSnapshot snapshot) => _snapshot = snapshot;

    /// <summary>The whole scrape as text.</summary>
    public static string Write(StateSnapshot snapshot, ConnectionStats stats, ScrapeDurations durations, MetricsOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(durations);
        ArgumentNullException.ThrowIfNull(options);

        var writer = new PrometheusWriter(snapshot);
        writer.WriteDaemon(stats);
        writer.WriteTpv();
        writer.WriteSky();
        if (options.Satellites)
        {
            writer.WriteSatellites();
        }

        writer.WriteTiming();
        writer.WriteExporter(stats, durations);
        return writer._text.ToString();
    }

    private void WriteDaemon(ConnectionStats stats)
    {
        Family(C.Up);
        Sample(stats.Up ? 1 : 0);

        Family(C.VersionInfo);
        if (_snapshot.Version is { } version)
        {
            Sample(1, ("release", version.Release), ("rev", version.Rev), ("proto_major", Label(version.ProtoMajor)), ("proto_minor", Label(version.ProtoMinor)));
        }

        Family(C.DeviceInfo);
        foreach (var device in _snapshot.Devices)
        {
            Sample(1, ("device", device.Path), ("driver", device.Info?.Driver), ("subtype", device.Info?.Subtype));
        }

        PerDevice(C.DeviceCycle, d => d.Info?.Cycle);
        PerDevice(C.DeviceBitsPerSecond, d => d.Info?.Bps);

        // Not subject to staleness: it says when data last arrived, which is the point when it is stale.
        Family(C.DeviceLastReport);
        foreach (var device in _snapshot.Devices)
        {
            foreach (var (cls, at) in device.LastReport)
            {
                Sample(UnixSeconds(at), ("device", device.Path), ("class", cls.Label()));
            }
        }
    }

    private void WriteTpv()
    {
        PerDevice(C.TpvMode, d => d.TpvMode);
        PerDevice(C.TpvTimestamp, d => d.TpvTimestamp is { } t ? UnixSeconds(t) : null);
        foreach (var group in TpvSeries.GroupBy(s => s.Metric))
        {
            Fields(group.Key, group, static (d, f) => d.Tpv(f));
        }
    }

    private void WriteSky()
    {
        PerDevice(C.SkySatellitesVisible, d => d.SatellitesVisible);
        PerDevice(C.SkySatellitesUsed, d => d.SatellitesUsed);
        Fields(C.SkyDop, SkySeries, static (d, f) => d.Sky(f));
    }

    private void WriteSatellites()
    {
        PerSatellite(C.SatelliteUsed, s => s.Used is { } used ? (used ? 1 : 0) : null);
        PerSatellite(C.SatelliteSnr, s => s.Fields[SatelliteField.Ss]);
        PerSatellite(C.SatelliteAzimuth, s => s.Fields[SatelliteField.Az]);
        PerSatellite(C.SatelliteElevation, s => s.Fields[SatelliteField.El]);
        PerSatellite(C.SatelliteHealth, s => s.Fields[SatelliteField.Health]);
        PerSatellite(C.SatelliteQuality, s => s.Fields[SatelliteField.Qual]);
        PerSatellite(C.SatellitePseudorangeResidual, s => s.Fields[SatelliteField.PrRes]);
    }

    private void WriteTiming()
    {
        PerDevice(C.PpsOffset, d => d.Pps?.OffsetSeconds);
        PerDevice(C.PpsPrecision, d => d.Pps?.PrecisionLog2Seconds);
        PerDevice(C.PpsQuantizationError, d => d.Pps?.QuantizationErrorSeconds);
        PerDevice(C.PpsPulseTimestamp, d => d.Pps?.PulseTimestampSeconds);

        Family(C.PpsPulses);
        foreach (var device in _snapshot.Devices)
        {
            if (device.PpsPulses is { } pulses)
            {
                Sample(pulses, ("device", device.Path));
            }
        }

        PerDevice(C.ToffOffset, d => d.Toff?.OffsetSeconds);
        PerDevice(C.ToffPrecision, d => d.Toff?.PrecisionLog2Seconds);
        PerDevice(C.OscRunning, d => Flag(d.Osc?.Running));
        PerDevice(C.OscReference, d => Flag(d.Osc?.Reference));
        PerDevice(C.OscDisciplined, d => Flag(d.Osc?.Disciplined));
        PerDevice(C.OscDelta, d => d.Osc?.DeltaSeconds);
    }

    /// <summary>Every counter writes every known label value, starting at 0, so rates work from the first scrape.</summary>
    private void WriteExporter(ConnectionStats stats, ScrapeDurations durations)
    {
        Family(C.BuildInfo);
        Sample(1, ("version", BuildInfo.Version), ("revision", BuildInfo.Revision), ("runtime", BuildInfo.Runtime));

        Family(C.ConnectionAttempts);
        foreach (var result in Enum.GetValues<ConnectResult>())
        {
            Sample(stats.Attempts(result), ("result", result.Label()));
        }

        Family(C.Disconnects);
        foreach (var reason in Enum.GetValues<DisconnectReason>())
        {
            Sample(stats.Disconnects(reason), ("reason", reason.Label()));
        }

        Family(C.ReconnectDelay);
        Sample(stats.ReconnectDelay.TotalSeconds);

        Family(C.ConnectedTimestamp);
        if (stats.ConnectedAt is { } connectedAt)
        {
            Sample(UnixSeconds(connectedAt));
        }

        Family(C.Messages);
        foreach (var cls in GpsdClassExtensions.All)
        {
            Sample(stats.Messages(cls), ("class", cls.Label()));
        }

        Family(C.ParseErrors);
        foreach (var reason in ParseErrorReasonExtensions.All)
        {
            Sample(stats.ParseErrors(reason), ("reason", reason.Label()));
        }

        Family(C.LastMessageTimestamp);
        if (stats.LastMessageAt is { } lastMessageAt)
        {
            Sample(UnixSeconds(lastMessageAt));
        }

        // A scrape's duration is recorded after it is written, so it shows from the next scrape;
        // before the first, the histogram has no samples and is left out.
        Family(C.ScrapeDuration);
        var (buckets, count, sum) = durations.Read();
        if (count > 0)
        {
            for (var i = 0; i < buckets.Length; i++)
            {
                Sample(C.ScrapeDuration.Name + "_bucket", buckets[i], ("le", Format(ScrapeDurations.Bounds[i])));
            }

            Sample(C.ScrapeDuration.Name + "_bucket", count, ("le", "+Inf"));
            Sample(C.ScrapeDuration.Name + "_sum", sum);
            Sample(C.ScrapeDuration.Name + "_count", count);
        }
    }

    private void PerDevice(MetricDefinition metric, Func<DeviceSnapshot, double?> value)
    {
        Family(metric);
        foreach (var device in _snapshot.Devices)
        {
            if (value(device) is { } v)
            {
                Sample(v, ("device", device.Path));
            }
        }
    }

    private void Fields<TField>(MetricDefinition metric, IEnumerable<FieldSeries<TField>> series, Func<DeviceSnapshot, TField, double?> read)
        where TField : struct, Enum
    {
        Family(metric);
        foreach (var device in _snapshot.Devices)
        {
            foreach (var s in series)
            {
                if (read(device, s.Field) is { } v)
                {
                    Sample(v / s.Divisor, ("device", device.Path), (s.LabelName, s.LabelValue));
                }
            }
        }
    }

    /// <summary>
    /// A satellite signal's series carries only the identity labels gpsd supplied: the GNSS
    /// labels when it sent <c>gnssid</c> and <c>svid</c>, <c>sigid</c> when it sent that too.
    /// </summary>
    private void PerSatellite(MetricDefinition metric, Func<Satellite, double?> value)
    {
        Family(metric);
        foreach (var device in _snapshot.Devices)
        {
            foreach (var satellite in device.Satellites)
            {
                if (value(satellite) is not { } v)
                {
                    continue;
                }

                var gnss = satellite is { GnssId: { } g, SvId: { } sv } ? (Id: g, Sv: sv) : default((int Id, int Sv)?);
                Sample(
                    v,
                    ("device", device.Path),
                    ("constellation", gnss is { } c ? Constellation.FromGnssId(c.Id) : null),
                    ("gnssid", Label(gnss?.Id)),
                    ("svid", Label(gnss?.Sv)),
                    ("sigid", gnss is null ? null : Label(satellite.SigId)),
                    ("prn", Label(satellite.Prn)));
            }
        }
    }

    /// <summary>Starts a family. Its HELP and TYPE lines are written with its first sample, so a family with none is left out.</summary>
    private void Family(MetricDefinition metric)
    {
        _family = metric;
        _headerWritten = false;
    }

    private void Sample(double value, params ReadOnlySpan<(string? Name, string? Value)> labels) =>
        Sample(_family!.Name, Format(value), labels);

    private void Sample(long value, params ReadOnlySpan<(string? Name, string? Value)> labels) =>
        Sample(_family!.Name, value.ToString(CultureInfo.InvariantCulture), labels);

    private void Sample(string name, double value, params ReadOnlySpan<(string? Name, string? Value)> labels) =>
        Sample(name, Format(value), labels);

    private void Sample(string name, long value, params ReadOnlySpan<(string? Name, string? Value)> labels) =>
        Sample(name, value.ToString(CultureInfo.InvariantCulture), labels);

    /// <summary>Writes one sample. A label with no name or no value (gpsd did not send it) is left off, not written empty.</summary>
    private void Sample(string name, string value, ReadOnlySpan<(string? Name, string? Value)> labels)
    {
        var family = _family!;
        if (!_headerWritten)
        {
            _text.Append("# HELP ").Append(family.Name).Append(' ');
            AppendEscaped(family.Help, escapeQuotes: false);
            _text.Append('\n');
            _text.Append("# TYPE ").Append(family.Name).Append(' ').Append(TypeName(family.Kind)).Append('\n');
            _headerWritten = true;
        }

        _text.Append(name);
        var first = true;
        foreach (var (labelName, labelValue) in labels)
        {
            if (string.IsNullOrEmpty(labelName) || string.IsNullOrEmpty(labelValue))
            {
                continue;
            }

            _text.Append(first ? '{' : ',').Append(labelName).Append("=\"");
            AppendEscaped(labelValue, escapeQuotes: true);
            _text.Append('"');
            first = false;
        }

        if (!first)
        {
            _text.Append('}');
        }

        _text.Append(' ').Append(value).Append('\n');
    }

    /// <summary>Label values escape backslash, double quote and line feed; HELP text escapes backslash and line feed.</summary>
    private void AppendEscaped(string text, bool escapeQuotes)
    {
        foreach (var ch in text)
        {
            _ = ch switch
            {
                '\\' => _text.Append(@"\\"),
                '\n' => _text.Append(@"\n"),
                '"' when escapeQuotes => _text.Append("\\\""),
                _ => _text.Append(ch),
            };
        }
    }

    private static string TypeName(MetricKind kind) => kind switch
    {
        MetricKind.Counter => "counter",
        MetricKind.Histogram => "histogram",
        _ => "gauge",
    };

    private static string Format(double value) => value switch
    {
        double.PositiveInfinity => "+Inf",
        double.NegativeInfinity => "-Inf",
        double.NaN => "NaN",
        _ => value.ToString("R", CultureInfo.InvariantCulture),
    };

    private static string? Label(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static double UnixSeconds(DateTimeOffset time) =>
        (time - DateTimeOffset.UnixEpoch).Ticks / (double)TimeSpan.TicksPerSecond;

    private static double? Flag(bool? value) => value is { } v ? (v ? 1 : 0) : null;

    private sealed record FieldSeries<TField>(TField Field, MetricDefinition Metric, string? LabelName = null, string? LabelValue = null, double Divisor = 1)
        where TField : struct, Enum;
}
