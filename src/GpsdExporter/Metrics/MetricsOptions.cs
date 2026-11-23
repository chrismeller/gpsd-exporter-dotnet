namespace GpsdExporter.Metrics;

/// <summary>The <c>Metrics</c> configuration section.</summary>
internal sealed class MetricsOptions
{
    public const string Section = "Metrics";

    /// <summary>The cardinality limit for each satellite instrument (AC6.7).</summary>
    public const int SatelliteCardinalityLimit = 4000;

    /// <summary>
    /// <c>Metrics:Satellites</c>: whether the per-satellite <c>gpsd_satellite_*</c> series are
    /// exposed. Turn off if their cardinality matters. Default true.
    /// </summary>
    public bool Satellites { get; set; } = true;
}
