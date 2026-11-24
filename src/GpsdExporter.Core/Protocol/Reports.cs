namespace GpsdExporter.Protocol;

/// <summary>A gpsd object the exporter understands. <see cref="Device"/> is the object's
/// <c>device</c> field, or null when gpsd did not send one.</summary>
public abstract record GpsdReport(string? Device);

/// <summary>VERSION. <see cref="Release"/> is null when gpsd sent no <c>release</c>.</summary>
public sealed record VersionReport(string? Release, string? Rev, int? ProtoMajor, int? ProtoMinor) : GpsdReport((string?)null);

public enum DeviceActivation
{
    /// <summary>No <c>activated</c> field.</summary>
    Absent,

    /// <summary><c>activated</c> is a timestamp (string, or a non-zero number from older gpsd).</summary>
    Activated,

    /// <summary><c>activated</c> is the number 0, which the daemon sends on deactivation.</summary>
    Deactivated,
}

/// <summary>A DEVICE object, alone or inside DEVICES. The device is identified by <c>path</c>.</summary>
public sealed record DeviceReport(string? Path, DeviceActivation Activation, string? Driver, string? Subtype, double? Cycle, double? Bps)
    : GpsdReport(Path);

public sealed record DevicesReport(IReadOnlyList<DeviceReport> Devices) : GpsdReport((string?)null);

public sealed record WatchReport(string? WatchDevice) : GpsdReport((string?)null);

public sealed record ErrorReport(string? Message) : GpsdReport((string?)null);

/// <summary>Any other class (AIS, GST, ATT, POLL, ACK, ...). The name is kept for logging only.</summary>
public sealed record OtherReport(string ClassName) : GpsdReport((string?)null);

/// <summary>TPV. <see cref="Time"/> is null when absent or unparseable.</summary>
public sealed record TpvReport(string? Device, int Mode, DateTimeOffset? Time, FieldValues<TpvField> Fields) : GpsdReport(Device);

/// <summary>
/// SKY. <see cref="Satellites"/> is null when the object had no <c>satellites</c> array
/// (a DOP-only report), and empty when the array was empty.
/// </summary>
public sealed record SkyReport(string? Device, DateTimeOffset? Time, FieldValues<SkyField> Fields, IReadOnlyList<Satellite>? Satellites)
    : GpsdReport(Device);

/// <summary>One satellite signal from a SKY <c>satellites</c> array.</summary>
public sealed record Satellite(int Prn, int? GnssId, int? SvId, int? SigId, bool? Used, FieldValues<SatelliteField> Fields);

/// <summary>
/// PPS or TOFF: the reference time (<c>real_*</c>) and the system clock (<c>clock_*</c>) as
/// integer seconds and nanoseconds, so offsets can be computed exactly.
/// </summary>
public sealed record TimeOffsetReport(
    string? Device,
    GpsdClass Kind,
    long RealSec,
    long RealNsec,
    long ClockSec,
    long ClockNsec,
    int? Precision,
    long? QErrPicoseconds) : GpsdReport(Device);

public sealed record OscReport(string? Device, bool? Running, bool? Reference, bool? Disciplined, double? DeltaNanoseconds) : GpsdReport(Device);

/// <summary>A field-level problem: the field was dropped, the rest of the object used.</summary>
public readonly record struct FieldError(ParseErrorReason Reason, string Field);

/// <summary>The outcome of parsing one line.</summary>
/// <param name="Class">The object's class, or null when the line had no usable class.</param>
/// <param name="Report">The report, or null when the line or object was rejected.</param>
/// <param name="Error">Why the whole line or object was rejected, if it was.</param>
/// <param name="FieldErrors">Fields and array entries that were dropped individually.</param>
public sealed record ParseResult(GpsdClass? Class, GpsdReport? Report, ParseErrorReason? Error, IReadOnlyList<FieldError> FieldErrors)
{
    public static ParseResult Rejected(ParseErrorReason reason, GpsdClass? cls = null, IReadOnlyList<FieldError>? fieldErrors = null) =>
        new(cls, null, reason, fieldErrors ?? []);
}
