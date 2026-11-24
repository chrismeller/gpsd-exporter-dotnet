using GpsdExporter.Health;

namespace GpsdExporter;

internal static class GpsdHealthCheckExtensions
{
    /// <summary>
    /// Adds <see cref="GpsdHealthCheck"/> to readiness (<c>/readyz</c>, the "ready" tag) only:
    /// a gpsd outage must never fail liveness (<c>/healthz</c>) and get the exporter restarted.
    /// </summary>
    public static TBuilder AddGpsdHealthCheck<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks().AddCheck<GpsdHealthCheck>(GpsdHealthCheck.Name, tags: ["ready"]);
        return builder;
    }
}
