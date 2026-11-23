namespace GpsdExporter.Metrics;

/// <summary>The <c>Metrics</c> configuration section.</summary>
internal sealed record MetricsOptions
{
    public const string Section = "Metrics";

    /// <summary>The cardinality limit for each satellite instrument (AC6.7).</summary>
    public const int SatelliteCardinalityLimit = 4000;

    /// <summary>
    /// <c>Metrics:Satellites</c>: whether the per-satellite <c>gpsd_satellite_*</c> series are
    /// exposed. Turn off if their cardinality matters. Default true.
    /// </summary>
    public bool Satellites { get; set; } = true;

    /// <summary>
    /// <c>Metrics:Runtime</c>: whether .NET runtime and process metrics are also on
    /// <c>/metrics</c>. They always go to OTLP when OTLP is on. Default false.
    /// </summary>
    public bool Runtime { get; set; }
}
