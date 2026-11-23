using GpsdExporter.Connection;
using GpsdExporter.Metrics;
using GpsdExporter.State;
using OpenTelemetry.Metrics;

namespace GpsdExporter;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// The gpsd side of the exporter: state, connection stats, the connection manager and the
    /// gpsd instruments on <c>/metrics</c>. Options use the spec defaults until configuration
    /// binding is added.
    /// </summary>
    public static IServiceCollection AddGpsdExporter(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new GpsdOptions());
        services.AddSingleton(new StalenessOptions());
        services.AddSingleton(new MetricsOptions());
        services.AddSingleton(sp => new StateStore(sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<StalenessOptions>()));
        services.AddSingleton<ConnectionStats>();
        services.AddHostedService<GpsdConnectionManager>();

        services.AddSingleton<ScrapeSnapshot>();
        services.AddSingleton<GpsdMetrics>();
        services.AddSingleton<ExporterMetrics>();
        services.AddOpenTelemetry().WithMetrics(metrics =>
        {
            metrics.AddMeter(GpsdMetrics.MeterName, ExporterMetrics.MeterName);
            foreach (var satellite in GpsdMetrics.SatelliteMetrics)
            {
                metrics.AddView(satellite.Name, new MetricStreamConfiguration { CardinalityLimit = MetricsOptions.SatelliteCardinalityLimit });
            }

            // Creating the instruments when the meter provider is built makes sure they exist
            // before the first scrape.
            metrics.AddInstrumentation(sp => sp.GetRequiredService<GpsdMetrics>());
            metrics.AddInstrumentation(sp => sp.GetRequiredService<ExporterMetrics>());
            metrics.AddPrometheusExporter(prometheus =>
            {
                // Exposed names are exactly the catalogue's (T7.3): no otel_scope_* labels, no
                // target_info, and no cached responses, so every scrape reflects current state.
                prometheus.ScopeInfoEnabled = false;
                prometheus.TargetInfoEnabled = false;
                prometheus.ScrapeResponseCacheDurationMilliseconds = 0;
            });
        });
        return services;
    }
}
