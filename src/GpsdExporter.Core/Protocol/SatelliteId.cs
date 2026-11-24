namespace GpsdExporter.Protocol;

/// <summary>
/// A satellite signal's identity within one device: <c>gnssid</c>, <c>svid</c> and
/// <c>sigid</c> when gpsd supplies <c>gnssid</c> and <c>svid</c>, otherwise <c>PRN</c>.
/// <c>sigid</c> may be absent from an otherwise full identity (receivers that track one signal).
/// </summary>
public readonly record struct SatelliteId
{
    private SatelliteId(int? gnssId, int? svId, int? sigId, int? prn)
    {
        GnssId = gnssId;
        SvId = svId;
        SigId = sigId;
        Prn = prn;
    }

    public int? GnssId { get; }

    public int? SvId { get; }

    public int? SigId { get; }

    /// <summary>Set only for a PRN-only identity.</summary>
    public int? Prn { get; }

    public static SatelliteId Of(Satellite satellite)
    {
        ArgumentNullException.ThrowIfNull(satellite);
        return satellite is { GnssId: { } gnss, SvId: { } sv }
            ? new SatelliteId(gnss, sv, satellite.SigId, null)
            : new SatelliteId(null, null, null, satellite.Prn);
    }
}

/// <summary>The <c>constellation</c> label, from <c>gnssid</c> (gpsd_json(5) gnssid table).</summary>
public static class Constellation
{
    public static IReadOnlyList<string> All { get; } =
        ["gps", "sbas", "galileo", "beidou", "imes", "qzss", "glonass", "navic", "unknown"];

    public static string FromGnssId(int gnssId) =>
        gnssId is >= 0 and <= 7 ? All[gnssId] : "unknown";
}
