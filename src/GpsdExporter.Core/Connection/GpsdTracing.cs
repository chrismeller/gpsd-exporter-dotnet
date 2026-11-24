using System.Diagnostics;

namespace GpsdExporter.Connection;

/// <summary>
/// The exporter's own spans. The host puts <see cref="SourceName"/> on the OpenTelemetry
/// tracer provider; without a listener, starting an activity costs nothing and returns null.
/// </summary>
public static class GpsdTracing
{
    public const string SourceName = "GpsdExporter";

    internal static readonly ActivitySource Source = new(SourceName);
}
