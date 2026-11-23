using System.Collections.Immutable;
using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// One scrape's view of the state (AC4.7): a single immutable <see cref="GpsdState"/> and a
/// single "now", both captured when the snapshot is taken, so every value read through it comes
/// from the same moment. Values older than their class's stale-after setting are not served
/// (AC4.3); staleness is worked out here, at read time, so there is no background sweeper.
/// </summary>
internal sealed class StateSnapshot
{
    private readonly StalenessOptions _staleness;

    public StateSnapshot(GpsdState state, DateTimeOffset now, StalenessOptions staleness)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(staleness);
        State = state;
        Now = now;
        _staleness = staleness;
        Devices = [.. state.Devices.Values.Select(d => new DeviceSnapshot(d, this))];
    }

    /// <summary>The raw state, with stale values still in it.</summary>
    public GpsdState State { get; }

    public DateTimeOffset Now { get; }

    /// <summary>The VERSION. It does not go stale; it goes with the connection (AC4.5).</summary>
    public VersionReport? Version => State.Version?.Value;

    /// <summary>Every tracked device, ordered by path.</summary>
    public ImmutableArray<DeviceSnapshot> Devices { get; }

    /// <summary>
    /// Whether a value of <paramref name="cls"/> received at <paramref name="receivedAt"/> is
    /// still served: its age is at most the class's stale-after setting.
    /// </summary>
    public bool IsFresh(GpsdClass cls, DateTimeOffset receivedAt) =>
        _staleness.For(cls) is not { } limit || Now - receivedAt <= limit;

    /// <summary>The value if it is still fresh, otherwise null.</summary>
    public T? Fresh<T>(GpsdClass cls, Timed<T>? value)
        where T : struct =>
        value is { } v && IsFresh(cls, v.ReceivedAt) ? v.Value : null;
}

/// <summary>One device as seen by a <see cref="StateSnapshot"/>: only fresh values are served.</summary>
internal sealed class DeviceSnapshot(DeviceState state, StateSnapshot snapshot)
{
    public DeviceState State { get; } = state;

    public string Path => State.Path;

    /// <summary>From DEVICES or DEVICE. It does not go stale; it goes when the device is removed.</summary>
    public DeviceInfo? Info => State.Info;

    /// <summary><c>gpsd_tpv_mode</c>, or null with no fresh TPV.</summary>
    public int? TpvMode => snapshot.Fresh(GpsdClass.Tpv, State.Tpv?.Mode);

    /// <summary><c>gpsd_tpv_timestamp_seconds</c>, or null.</summary>
    public DateTimeOffset? TpvTimestamp => snapshot.Fresh(GpsdClass.Tpv, State.Tpv?.Timestamp);

    /// <summary>Every served, fresh TPV field.</summary>
    public IEnumerable<(TpvField Field, double Value)> TpvFields => Fresh(GpsdClass.Tpv, State.Tpv?.Served);

    public double? Tpv(TpvField field) => snapshot.Fresh(GpsdClass.Tpv, State.Tpv?[field]);

    /// <summary>Every served, fresh scalar SKY field.</summary>
    public IEnumerable<(SkyField Field, double Value)> SkyFields => Fresh(GpsdClass.Sky, State.Sky?.Epochs.Served);

    public double? Sky(SkyField field) => snapshot.Fresh(GpsdClass.Sky, State.Sky?[field]);

    public double? SatellitesVisible => snapshot.Fresh(GpsdClass.Sky, State.Sky?.Visible);

    public double? SatellitesUsed => snapshot.Fresh(GpsdClass.Sky, State.Sky?.Used);

    /// <summary>The latest satellite set if it is fresh, otherwise empty.</summary>
    public ImmutableArray<Satellite> Satellites =>
        snapshot.Fresh(GpsdClass.Sky, State.Sky?.Satellites) ?? [];

    public TimeOffsetState? Pps => FreshState(GpsdClass.Pps, State.Pps, State.Pps?.ReceivedAt);

    public TimeOffsetState? Toff => FreshState(GpsdClass.Toff, State.Toff, State.Toff?.ReceivedAt);

    public OscState? Osc => FreshState(GpsdClass.Osc, State.Osc, State.Osc?.ReceivedAt);

    /// <summary>
    /// <c>gpsd_pps_pulses_total</c>. A counter, so it is not subject to staleness; it goes with
    /// the device (AC4.7). Null before the device's first PPS.
    /// </summary>
    public long? PpsPulses => State.PpsPulses > 0 ? State.PpsPulses : null;

    /// <summary>When each class was last received from this device.</summary>
    public ImmutableSortedDictionary<GpsdClass, DateTimeOffset> LastReport => State.LastReport;

    private T? FreshState<T>(GpsdClass cls, T? value, DateTimeOffset? receivedAt)
        where T : class =>
        value is not null && receivedAt is { } at && snapshot.IsFresh(cls, at) ? value : null;

    private IEnumerable<(TField Field, double Value)> Fresh<TField>(GpsdClass cls, IEnumerable<(TField Field, Timed<double> Value)>? values)
    {
        if (values is null)
        {
            yield break;
        }

        foreach (var (field, value) in values)
        {
            if (snapshot.IsFresh(cls, value.ReceivedAt))
            {
                yield return (field, value.Value);
            }
        }
    }
}
