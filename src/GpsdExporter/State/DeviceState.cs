using System.Collections.Immutable;
using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>What gpsd said about a device in DEVICES or DEVICE (<c>gpsd_device_*</c>).</summary>
internal sealed record DeviceInfo(string? Driver, string? Subtype, double? Cycle, double? Bps, DateTimeOffset ReceivedAt);

/// <summary>Everything known about one device. Immutable.</summary>
/// <param name="Path">The device path, which is the <c>device</c> label.</param>
internal sealed record DeviceState(string Path)
{
    /// <summary>From DEVICES or DEVICE; null for a device created implicitly by another report (AC6.6).</summary>
    public DeviceInfo? Info { get; init; }

    /// <summary>Null until the device's first TPV (AC3.4).</summary>
    public TpvState? Tpv { get; init; }

    /// <summary>Null until the device's first SKY (AC3.4).</summary>
    public SkyState? Sky { get; init; }

    /// <summary>When each class was last received from this device (<c>gpsd_device_last_report_timestamp_seconds</c>).</summary>
    public ImmutableSortedDictionary<GpsdClass, DateTimeOffset> LastReport { get; init; } =
        ImmutableSortedDictionary<GpsdClass, DateTimeOffset>.Empty;
}
