using System.Collections.Immutable;
using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>What gpsd said about a device in DEVICES or DEVICE (<c>gpsd_device_*</c>).</summary>
public sealed record DeviceInfo(string? Driver, string? Subtype, double? Cycle, double? Bps, DateTimeOffset ReceivedAt);

/// <summary>Everything known about one device. Immutable.</summary>
/// <param name="Path">The device path, which is the <c>device</c> label.</param>
public sealed record DeviceState(string Path)
{
    /// <summary>From DEVICES or DEVICE; null for a device created implicitly by another report.</summary>
    public DeviceInfo? Info { get; init; }

    /// <summary>Null until the device's first TPV.</summary>
    public TpvState? Tpv { get; init; }

    /// <summary>Null until the device's first SKY.</summary>
    public SkyState? Sky { get; init; }

    /// <summary>The latest PPS; null until the device's first.</summary>
    public TimeOffsetState? Pps { get; init; }

    /// <summary>The latest TOFF; null until the device's first.</summary>
    public TimeOffsetState? Toff { get; init; }

    /// <summary>The latest OSC; null until the device's first.</summary>
    public OscState? Osc { get; init; }

    /// <summary>
    /// PPS objects received from this device on this connection (<c>gpsd_pps_pulses_total</c>).
    /// Starts again from 0 when the device is removed or the connection is reset.
    /// </summary>
    public long PpsPulses { get; init; }

    /// <summary>When each class was last received from this device (<c>gpsd_device_last_report_timestamp_seconds</c>).</summary>
    public ImmutableSortedDictionary<GpsdClass, DateTimeOffset> LastReport { get; init; } =
        ImmutableSortedDictionary<GpsdClass, DateTimeOffset>.Empty;
}
