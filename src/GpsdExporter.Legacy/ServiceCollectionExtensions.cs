using GpsdExporter.Configuration;
using GpsdExporter.Connection;
using GpsdExporter.Metrics;
using GpsdExporter.State;
using GpsdExporter.Telemetry;
using OpenTelemetry.Metrics;

namespace GpsdExporter;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// The gpsd side of the exporter: state, connection stats, the connection manager and the
    /// instruments on <c>/metrics</c>, configured from validated <paramref name="settings"/>.
    /// </summary>
    public static IServiceCollection AddGpsdExporter(this IServiceCollection services, ExporterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(settings.Gpsd);
        services.AddSingleton(settings.Staleness);
        services.AddSingleton(settings.Metrics);
        services.AddSingleton(settings.Http);
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
            if (settings.Metrics.Runtime)
            {
                metrics.AddRuntimeInstrumentation().AddProcessInstrumentation();
            }

            metrics.AddPrometheusExporter(prometheus =>
            {
                // Exposed names are exactly the catalogue's (T7.3): no otel_scope_* labels, no
                // target_info, and no cached responses, so every scrape reflects current state.
                prometheus.ScopeInfoEnabled = false;
                prometheus.TargetInfoEnabled = false;
                prometheus.ScrapeResponseCacheDurationMilliseconds = 0;
            });
        });
        services.AddOwnTelemetry(settings.Otlp);
        return services;
    }
}
