namespace GpsdExporter.Configuration;

/// <summary>The <c>Http</c> configuration section.</summary>
internal sealed class HttpOptions
{
    public const string Section = "Http";

    /// <summary><c>[addr]:port</c>, as the Go exporter's <c>-l</c>: <c>:9978</c> listens on every address.</summary>
    public string Listen { get; set; } = ":9978";

    public string MetricsPath { get; set; } = "/metrics";
}
