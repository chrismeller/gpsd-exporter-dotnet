using GpsdExporter.Connection;
using OpenTelemetry.Trace;

namespace GpsdExporter.Telemetry;

internal static class ExporterTracingExtensions
{
    /// <summary>Puts the exporter's own spans (<see cref="GpsdTracing"/>) on the OpenTelemetry tracer provider.</summary>
    public static TBuilder AddExporterTracing<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(GpsdTracing.SourceName));
        return builder;
    }
}
