using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// The latest PPS or TOFF for a device. Each report replaces the previous one (R4).
/// </summary>
internal sealed record TimeOffsetState(TimeOffsetReport Report, DateTimeOffset ReceivedAt)
{
    private const long NanosecondsPerSecond = 1_000_000_000;

    /// <summary>
    /// System clock minus reference time, in integer nanoseconds: positive means the system
    /// clock is ahead. Computed exactly, before any conversion to floating point (T3.5).
    /// </summary>
    public Int128 OffsetNanoseconds =>
        ((Int128)Report.ClockSec - Report.RealSec) * NanosecondsPerSecond + (Report.ClockNsec - Report.RealNsec);

    /// <summary><c>gpsd_pps_offset_seconds</c> and <c>gpsd_toff_offset_seconds</c>.</summary>
    public double OffsetSeconds => (double)OffsetNanoseconds / NanosecondsPerSecond;

    /// <summary><c>precision</c> as gpsd sends it: NTP style, log₂ seconds.</summary>
    public int? PrecisionLog2Seconds => Report.Precision;

    /// <summary><c>qErr</c> (picoseconds) in seconds; PPS only, and only when sent (T3.4).</summary>
    public double? QuantizationErrorSeconds => Report.QErrPicoseconds is { } ps ? ps / 1e12 : null;

    /// <summary><c>gpsd_pps_pulse_timestamp_seconds</c>: the system clock at the pulse.</summary>
    public double PulseTimestampSeconds => Report.ClockSec + (Report.ClockNsec / (double)NanosecondsPerSecond);
}

/// <summary>The latest OSC for a device. Each report replaces the previous one (R4).</summary>
internal sealed record OscState(OscReport Report, DateTimeOffset ReceivedAt)
{
    public bool? Running => Report.Running;

    public bool? Reference => Report.Reference;

    public bool? Disciplined => Report.Disciplined;

    /// <summary><c>delta</c> (nanoseconds) in seconds.</summary>
    public double? DeltaSeconds => Report.DeltaNanoseconds is { } ns ? ns / 1e9 : null;
}
