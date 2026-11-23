namespace GpsdExporter.Metrics;

internal static class MetricsEndpoint
{
    public const string DefaultPath = "/metrics";

    /// <summary>
    /// Maps the Prometheus scrape endpoint. Each scrape pins one <see cref="ScrapeSnapshot"/>
    /// before the exporter collects, so every gpsd series in it comes from the same state, and
    /// records how long the scrape took in <c>gpsd_exporter_scrape_duration_seconds</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapGpsdMetrics(this IEndpointRouteBuilder endpoints, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var scrape = endpoints.ServiceProvider.GetRequiredService<ScrapeSnapshot>();
        var exporterMetrics = endpoints.ServiceProvider.GetRequiredService<ExporterMetrics>();
        var time = endpoints.ServiceProvider.GetRequiredService<TimeProvider>();

        return endpoints.MapPrometheusScrapingEndpoint(
            path,
            meterProvider: null,
            configureBranchedPipeline: pipeline => pipeline.Use(async (context, next) =>
            {
                using var pin = await scrape.PinAsync(context.RequestAborted).ConfigureAwait(false);
                var started = time.GetTimestamp();
                await next(context).ConfigureAwait(false);
                exporterMetrics.RecordScrape(time.GetElapsedTime(started));
            }),
            optionsName: null);
    }
}
