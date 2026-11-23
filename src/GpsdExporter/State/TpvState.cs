using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>One TPV epoch: the TPVs that share a <c>time</c>, merged.</summary>
/// <param name="Time">The epoch's <c>time</c>, or null for an untimed TPV, which is an epoch of its own.</param>
/// <param name="ReceivedAt">When the latest TPV of the epoch was received.</param>
internal sealed record TpvEpoch(DateTimeOffset? Time, DateTimeOffset ReceivedAt, TimedFields<TpvField> Fields);

/// <summary>
/// A device's TPV state under the spec's merge rules (R4): a field is served if it was present
/// in the current epoch or the one before it, and fix loss removes fields at once (AC4.1).
/// Immutable; <see cref="Apply"/> returns a new state.
/// </summary>
internal sealed record TpvState(Timed<int> Mode, TpvEpoch Current, TpvEpoch? Previous)
{
    /// <summary>Fields served only with a 2D or 3D fix (the spec's "mode ≥ 2" rows).</summary>
    public static readonly TpvField[] FixFields =
    [
        TpvField.Lat, TpvField.Lon,
        TpvField.Speed, TpvField.Track, TpvField.MagTrack,
        TpvField.VelN, TpvField.VelE, TpvField.VelD,
        TpvField.EcefX, TpvField.EcefY, TpvField.EcefZ,
        TpvField.EcefVx, TpvField.EcefVy, TpvField.EcefVz,
        TpvField.EcefPAcc, TpvField.EcefVAcc,
        TpvField.RelN, TpvField.RelE, TpvField.RelD,
        TpvField.Eph, TpvField.Epy, TpvField.Epx, TpvField.Sep,
        TpvField.Eps, TpvField.Epd,
    ];

    /// <summary>Fields served only with a 3D fix (the spec's "mode 3" rows): altitude, climb and vertical error.</summary>
    public static readonly TpvField[] ThreeDFields =
    [
        TpvField.AltHae, TpvField.AltMsl, TpvField.Climb, TpvField.Epv, TpvField.Epc,
    ];

    /// <summary>The served value of a field: from the current epoch, else the previous one.</summary>
    public Timed<double>? this[TpvField field] => Current.Fields[field] ?? Previous?.Fields[field];

    /// <summary>Every served field, in enum order.</summary>
    public IEnumerable<(TpvField Field, Timed<double> Value)> Served =>
        FieldIndex<TpvField>.All
            .Select(f => (Field: f, Value: this[f]))
            .Where(p => p.Value.HasValue)
            .Select(p => (p.Field, p.Value!.Value));

    /// <summary>
    /// The fix time (<c>gpsd_tpv_timestamp_seconds</c>): the epoch's <c>time</c>, served with a
    /// 2D or 3D fix only.
    /// </summary>
    public Timed<DateTimeOffset>? Timestamp
    {
        get
        {
            if (Mode.Value < 2)
            {
                return null;
            }

            var epoch = Current.Time is not null ? Current : Previous;
            return epoch?.Time is { } t ? new Timed<DateTimeOffset>(t, epoch.ReceivedAt) : null;
        }
    }

    /// <summary>Applies one TPV to <paramref name="state"/>, which is null before the device's first TPV.</summary>
    public static TpvState Apply(TpvState? state, TpvReport report, DateTimeOffset receivedAt)
    {
        TpvEpoch current;
        TpvEpoch? previous;
        if (state is not null && report.Time is { } time && state.Current.Time == time)
        {
            // Another TPV of the same epoch.
            current = state.Current with { ReceivedAt = receivedAt, Fields = state.Current.Fields.Merge(report.Fields, receivedAt) };
            previous = state.Previous;
        }
        else
        {
            // A later time, an earlier time (a receiver reset), or no time: a new epoch.
            current = new TpvEpoch(report.Time, receivedAt, TimedFields<TpvField>.From(report.Fields, receivedAt));
            previous = state?.Current;
        }

        var dropped = report.Mode switch
        {
            < 2 => [.. FixFields, .. ThreeDFields],
            2 => ThreeDFields,
            _ => [],
        };

        if (dropped.Length > 0)
        {
            current = current with { Fields = current.Fields.Without(dropped) };
            previous = previous is null ? null : previous with { Fields = previous.Fields.Without(dropped) };
        }

        return new TpvState(new Timed<int>(report.Mode, receivedAt), current, previous);
    }
}
