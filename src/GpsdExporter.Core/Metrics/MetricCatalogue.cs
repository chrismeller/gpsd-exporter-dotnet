using System.Collections.Immutable;

namespace GpsdExporter.Metrics;

/// <summary>The Prometheus type of a metric.</summary>
public enum MetricKind
{
    Gauge,
    Counter,
    Histogram,
}

/// <summary>
/// One metric as exposed on <c>/metrics</c>: the exact name, type, unit, label names and help
/// text. <see cref="Unit"/> is the unit the name's suffix stands for, in UCUM-style
/// notation, or <c>1</c> for a dimensionless value. It is documentation and a test input;
/// the exposed name already carries the unit, so nothing is appended to it.
/// </summary>
public sealed record MetricDefinition(string Name, MetricKind Kind, string Unit, ImmutableArray<string> Labels, string Help);

/// <summary>
/// Every metric the exporter exposes, declared once and by hand, so the exposed
/// names are a deliberate contract. Nothing is derived from JSON keys or by reflection.
/// </summary>
public static class MetricCatalogue
{
    private const string UnknownConfidence =
        " gpsd does not say which confidence level the receiver uses (95%, 50%, 1 sigma or 2 sigma).";

    private static readonly ImmutableArray<string> None = [];
    private static readonly ImmutableArray<string> Device = ["device"];
    private static readonly ImmutableArray<string> DeviceAxis = ["device", "axis"];
    private static readonly ImmutableArray<string> DeviceKind = ["device", "kind"];

    /// <summary>
    /// The satellite labels. A series carries only the ones gpsd supplied: <c>device</c>
    /// and <c>prn</c> always, <c>constellation</c>, <c>gnssid</c> and <c>svid</c> when gpsd sent
    /// <c>gnssid</c> and <c>svid</c>, and <c>sigid</c> when it sent that too.
    /// </summary>
    public static readonly ImmutableArray<string> SatelliteLabels = ["device", "constellation", "gnssid", "svid", "sigid", "prn"];

    // Connection and daemon.
    public static readonly MetricDefinition Up = Gauge("gpsd_up", "1", None,
        "Whether the exporter is connected to gpsd and has received VERSION (1) or not (0).");

    public static readonly MetricDefinition VersionInfo = Gauge("gpsd_version_info", "1", ["release", "rev", "proto_major", "proto_minor"],
        "gpsd daemon version, from VERSION. Always 1.");

    public static readonly MetricDefinition DeviceInfo = Gauge("gpsd_device_info", "1", ["device", "driver", "subtype"],
        "A device reported by gpsd. Always 1.");

    public static readonly MetricDefinition DeviceCycle = Gauge("gpsd_device_cycle_seconds", "s", Device,
        "Device reporting cycle, from DEVICE cycle.");

    public static readonly MetricDefinition DeviceBitsPerSecond = Gauge("gpsd_device_bits_per_second", "bit/s", Device,
        "Device serial speed, from DEVICE bps.");

    public static readonly MetricDefinition DeviceLastReport = Gauge("gpsd_device_last_report_timestamp_seconds", "s", ["device", "class"],
        "When the exporter last received a report of this class from this device, by the exporter's clock.");

    // TPV.
    public static readonly MetricDefinition TpvMode = Gauge("gpsd_tpv_mode", "1", Device,
        "NMEA mode from TPV mode: 0 unknown, 1 no fix, 2 2D fix, 3 3D fix.");

    public static readonly MetricDefinition TpvStatus = Gauge("gpsd_tpv_status", "1", Device,
        "GPS fix status from TPV status, as gpsd numbers it (2 DGPS, 3 RTK fixed, 4 RTK float, ...).");

    public static readonly MetricDefinition TpvTimestamp = Gauge("gpsd_tpv_timestamp_seconds", "s", Device,
        "Time of the fix from TPV time, in seconds since the Unix epoch.");

    public static readonly MetricDefinition TpvLeapSeconds = Gauge("gpsd_tpv_leap_seconds", "s", Device,
        "Current leap seconds (GPS minus UTC), from TPV leapseconds.");

    public static readonly MetricDefinition TpvLatitude = Gauge("gpsd_tpv_latitude_degrees", "deg", Device,
        "Latitude in degrees, positive north, from TPV lat.");

    public static readonly MetricDefinition TpvLongitude = Gauge("gpsd_tpv_longitude_degrees", "deg", Device,
        "Longitude in degrees, positive east, from TPV lon.");

    public static readonly MetricDefinition TpvAltitudeHae = Gauge("gpsd_tpv_altitude_hae_meters", "m", Device,
        "Altitude above the WGS84 ellipsoid, from TPV altHAE.");

    public static readonly MetricDefinition TpvAltitudeMsl = Gauge("gpsd_tpv_altitude_msl_meters", "m", Device,
        "Altitude above mean sea level, from TPV altMSL.");

    public static readonly MetricDefinition TpvGeoidSeparation = Gauge("gpsd_tpv_geoid_separation_meters", "m", Device,
        "Geoid separation (MSL minus ellipsoid height), from TPV geoidSep.");

    public static readonly MetricDefinition TpvSpeed = Gauge("gpsd_tpv_speed_meters_per_second", "m/s", Device,
        "Speed over ground, from TPV speed.");

    public static readonly MetricDefinition TpvClimb = Gauge("gpsd_tpv_climb_meters_per_second", "m/s", Device,
        "Climb (positive) or sink (negative) rate, from TPV climb.");

    public static readonly MetricDefinition TpvTrack = Gauge("gpsd_tpv_track_degrees", "deg", Device,
        "Course over ground relative to true north, from TPV track.");

    public static readonly MetricDefinition TpvMagneticTrack = Gauge("gpsd_tpv_magnetic_track_degrees", "deg", Device,
        "Course over ground relative to magnetic north, from TPV magtrack.");

    public static readonly MetricDefinition TpvMagneticVariation = Gauge("gpsd_tpv_magnetic_variation_degrees", "deg", Device,
        "Magnetic variation, from TPV magvar.");

    public static readonly MetricDefinition TpvVelocity = Gauge("gpsd_tpv_velocity_meters_per_second", "m/s", DeviceAxis,
        "Velocity component (north, east, down), from TPV velN, velE and velD.");

    public static readonly MetricDefinition TpvEcefPosition = Gauge("gpsd_tpv_ecef_position_meters", "m", DeviceAxis,
        "ECEF position component (x, y, z), from TPV ecefx, ecefy and ecefz.");

    public static readonly MetricDefinition TpvEcefVelocity = Gauge("gpsd_tpv_ecef_velocity_meters_per_second", "m/s", DeviceAxis,
        "ECEF velocity component (x, y, z), from TPV ecefvx, ecefvy and ecefvz.");

    public static readonly MetricDefinition TpvEcefPositionError = Gauge("gpsd_tpv_ecef_position_error_meters", "m", Device,
        "ECEF position error estimate, from TPV ecefpAcc." + UnknownConfidence);

    public static readonly MetricDefinition TpvEcefVelocityError = Gauge("gpsd_tpv_ecef_velocity_error_meters_per_second", "m/s", Device,
        "ECEF velocity error estimate, from TPV ecefvAcc." + UnknownConfidence);

    public static readonly MetricDefinition TpvRelativePosition = Gauge("gpsd_tpv_relative_position_meters", "m", DeviceAxis,
        "Position relative to the base station (north, east, down), from TPV relN, relE and relD.");

    public static readonly MetricDefinition TpvPositionError = Gauge("gpsd_tpv_position_error_meters", "m", DeviceKind,
        "Position error estimate (horizontal eph, vertical epv, latitude epy, longitude epx, spherical sep)." + UnknownConfidence);

    public static readonly MetricDefinition TpvTimeError = Gauge("gpsd_tpv_time_error_seconds", "s", Device,
        "Time error estimate, from TPV ept." + UnknownConfidence);

    public static readonly MetricDefinition TpvSpeedError = Gauge("gpsd_tpv_speed_error_meters_per_second", "m/s", Device,
        "Speed error estimate, from TPV eps." + UnknownConfidence);

    public static readonly MetricDefinition TpvClimbError = Gauge("gpsd_tpv_climb_error_meters_per_second", "m/s", Device,
        "Climb error estimate, from TPV epc." + UnknownConfidence);

    public static readonly MetricDefinition TpvTrackError = Gauge("gpsd_tpv_track_error_degrees", "deg", Device,
        "Track error estimate, from TPV epd." + UnknownConfidence);

    public static readonly MetricDefinition TpvClockBias = Gauge("gpsd_tpv_clock_bias_seconds", "s", Device,
        "Receiver clock bias, from TPV clockbias (nanoseconds) converted to seconds.");

    public static readonly MetricDefinition TpvClockDrift = Gauge("gpsd_tpv_clock_drift_seconds_per_second", "s/s", Device,
        "Receiver clock drift, from TPV clockdrift (nanoseconds per second) converted to seconds per second.");

    public static readonly MetricDefinition TpvDgpsAge = Gauge("gpsd_tpv_dgps_age_seconds", "s", Device,
        "Age of the DGPS data, from TPV dgpsAge.");

    public static readonly MetricDefinition TpvDgpsStation = Gauge("gpsd_tpv_dgps_station", "1", Device,
        "DGPS station id, from TPV dgpsSta.");

    public static readonly MetricDefinition TpvJamming = Gauge("gpsd_tpv_jamming_indicator", "1", Device,
        "Jamming indicator, 0 (none) to 255 (strong), from TPV jam.");

    public static readonly MetricDefinition TpvAntenna = Gauge("gpsd_tpv_antenna_status", "1", Device,
        "Antenna status code, from TPV ant: 0 unknown, 1 ok, 2 open, 3 short.");

    public static readonly MetricDefinition TpvAntennaPower = Gauge("gpsd_tpv_antenna_power_status", "1", Device,
        "Antenna power status code, from TPV antPwr: 0 unknown, 1 off, 2 on.");

    public static readonly MetricDefinition TpvTemperature = Gauge("gpsd_tpv_receiver_temperature_celsius", "Cel", Device,
        "Receiver temperature, from TPV temp.");

    // SKY.
    public static readonly MetricDefinition SkySatellitesVisible = Gauge("gpsd_sky_satellites_visible", "1", Device,
        "Satellite signals in the sky view, from SKY nSat or the length of the satellites array.");

    public static readonly MetricDefinition SkySatellitesUsed = Gauge("gpsd_sky_satellites_used", "1", Device,
        "Satellite signals used in the fix, from SKY uSat or the satellites marked used.");

    public static readonly MetricDefinition SkyDop = Gauge("gpsd_sky_dop", "1", DeviceKind,
        "Dilution of precision (geometric, horizontal, position, time, vertical, longitudinal, latitudinal), from SKY gdop, hdop, pdop, tdop, vdop, xdop and ydop.");

    public static readonly MetricDefinition SatelliteUsed = Gauge("gpsd_satellite_used", "1", SatelliteLabels,
        "Whether the satellite signal is used in the fix (1) or not (0).");

    public static readonly MetricDefinition SatelliteSnr = Gauge("gpsd_satellite_snr_dbhz", "dBHz", SatelliteLabels,
        "Signal strength (carrier to noise density), from ss.");

    public static readonly MetricDefinition SatelliteAzimuth = Gauge("gpsd_satellite_azimuth_degrees", "deg", SatelliteLabels,
        "Azimuth from true north, from az.");

    public static readonly MetricDefinition SatelliteElevation = Gauge("gpsd_satellite_elevation_degrees", "deg", SatelliteLabels,
        "Elevation above the horizon, from el.");

    public static readonly MetricDefinition SatelliteHealth = Gauge("gpsd_satellite_health", "1", SatelliteLabels,
        "Satellite health, from health: 0 unknown, 1 ok, 2 unhealthy.");

    public static readonly MetricDefinition SatelliteQuality = Gauge("gpsd_satellite_quality", "1", SatelliteLabels,
        "Signal quality indicator, 0 to 7, from qual.");

    public static readonly MetricDefinition SatellitePseudorangeResidual = Gauge("gpsd_satellite_pseudorange_residual_meters", "m", SatelliteLabels,
        "Pseudorange residual, from prRes.");

    // PPS, TOFF and OSC.
    public static readonly MetricDefinition PpsOffset = Gauge("gpsd_pps_offset_seconds", "s", Device,
        "System clock minus the PPS reference time at the last pulse; positive means the system clock is ahead.");

    public static readonly MetricDefinition PpsPrecision = Gauge("gpsd_pps_precision_log2_seconds", "log2(s)", Device,
        "PPS precision as log2 seconds (NTP style), from PPS precision.");

    public static readonly MetricDefinition PpsQuantizationError = Gauge("gpsd_pps_quantization_error_seconds", "s", Device,
        "PPS quantization error, from PPS qErr (picoseconds) converted to seconds.");

    public static readonly MetricDefinition PpsPulseTimestamp = Gauge("gpsd_pps_pulse_timestamp_seconds", "s", Device,
        "System clock at the last pulse, from PPS clock_sec and clock_nsec.");

    public static readonly MetricDefinition PpsPulses = Counter("gpsd_pps_pulses_total", "1", Device,
        "PPS reports received from the device on the current connection.");

    public static readonly MetricDefinition ToffOffset = Gauge("gpsd_toff_offset_seconds", "s", Device,
        "System clock minus the GPS time in the last TOFF; positive means the system clock is ahead.");

    public static readonly MetricDefinition ToffPrecision = Gauge("gpsd_toff_precision_log2_seconds", "log2(s)", Device,
        "TOFF precision as log2 seconds (NTP style), from TOFF precision.");

    public static readonly MetricDefinition OscRunning = Gauge("gpsd_osc_running", "1", Device,
        "Whether the oscillator is running (1) or not (0), from OSC running.");

    public static readonly MetricDefinition OscReference = Gauge("gpsd_osc_reference", "1", Device,
        "Whether the oscillator has a PPS reference (1) or not (0), from OSC reference.");

    public static readonly MetricDefinition OscDisciplined = Gauge("gpsd_osc_disciplined", "1", Device,
        "Whether the oscillator is disciplined to the reference (1) or not (0), from OSC disciplined.");

    public static readonly MetricDefinition OscDelta = Gauge("gpsd_osc_delta_seconds", "s", Device,
        "Time difference between the oscillator and the PPS reference, from OSC delta (nanoseconds) converted to seconds.");

    // The exporter's own metrics.
    public static readonly MetricDefinition BuildInfo = Gauge("gpsd_exporter_build_info", "1", ["version", "revision", "runtime"],
        "Build information. Always 1.");

    public static readonly MetricDefinition ConnectionAttempts = Counter("gpsd_exporter_connection_attempts_total", "1", ["result"],
        "Connection attempts to gpsd, by result.");

    public static readonly MetricDefinition Disconnects = Counter("gpsd_exporter_disconnects_total", "1", ["reason"],
        "Connections to gpsd lost or closed, by reason.");

    public static readonly MetricDefinition ReconnectDelay = Gauge("gpsd_exporter_reconnect_delay_seconds", "s", None,
        "Current reconnect backoff delay; 0 while connected.");

    public static readonly MetricDefinition ConnectedTimestamp = Gauge("gpsd_exporter_connected_timestamp_seconds", "s", None,
        "When the current connection to gpsd was established. Present only while connected.");

    public static readonly MetricDefinition Messages = Counter("gpsd_exporter_messages_total", "1", ["class"],
        "gpsd objects received, by class.");

    public static readonly MetricDefinition ParseErrors = Counter("gpsd_exporter_parse_errors_total", "1", ["reason"],
        "Input lines or fields skipped, by reason.");

    public static readonly MetricDefinition LastMessageTimestamp = Gauge("gpsd_exporter_last_message_timestamp_seconds", "s", None,
        "When any line was last received from gpsd. Present only after the first line.");

    public static readonly MetricDefinition ScrapeDuration = Histogram("gpsd_exporter_scrape_duration_seconds", "s", None,
        "Time to collect the gpsd metrics for a scrape.");

    /// <summary>Every metric, grouped by area in a fixed order.</summary>
    public static ImmutableArray<MetricDefinition> All { get; } =
    [
        Up, VersionInfo, DeviceInfo, DeviceCycle, DeviceBitsPerSecond, DeviceLastReport,
        TpvMode, TpvStatus, TpvTimestamp, TpvLeapSeconds, TpvLatitude, TpvLongitude, TpvAltitudeHae, TpvAltitudeMsl,
        TpvGeoidSeparation, TpvSpeed, TpvClimb, TpvTrack, TpvMagneticTrack, TpvMagneticVariation, TpvVelocity,
        TpvEcefPosition, TpvEcefVelocity, TpvEcefPositionError, TpvEcefVelocityError, TpvRelativePosition,
        TpvPositionError, TpvTimeError, TpvSpeedError, TpvClimbError, TpvTrackError, TpvClockBias, TpvClockDrift,
        TpvDgpsAge, TpvDgpsStation, TpvJamming, TpvAntenna, TpvAntennaPower, TpvTemperature,
        SkySatellitesVisible, SkySatellitesUsed, SkyDop,
        SatelliteUsed, SatelliteSnr, SatelliteAzimuth, SatelliteElevation, SatelliteHealth, SatelliteQuality,
        SatellitePseudorangeResidual,
        PpsOffset, PpsPrecision, PpsQuantizationError, PpsPulseTimestamp, PpsPulses, ToffOffset, ToffPrecision,
        OscRunning, OscReference, OscDisciplined, OscDelta,
        BuildInfo, ConnectionAttempts, Disconnects, ReconnectDelay, ConnectedTimestamp, Messages, ParseErrors,
        LastMessageTimestamp, ScrapeDuration,
    ];

    private static MetricDefinition Gauge(string name, string unit, ImmutableArray<string> labels, string help) =>
        new(name, MetricKind.Gauge, unit, labels, help);

    private static MetricDefinition Counter(string name, string unit, ImmutableArray<string> labels, string help) =>
        new(name, MetricKind.Counter, unit, labels, help);

    private static MetricDefinition Histogram(string name, string unit, ImmutableArray<string> labels, string help) =>
        new(name, MetricKind.Histogram, unit, labels, help);
}
