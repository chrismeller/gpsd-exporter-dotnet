namespace GpsdExporter.Metrics;

internal static class MetricsEndpoint
{
    public const string DefaultPath = "/metrics";

    /// <summary>
    /// Maps the Prometheus scrape endpoint. Each scrape pins one <see cref="ScrapeSnapshot"/>
    /// before the exporter collects, so every gpsd series in it comes from the same state.
    /// </summary>
    public static IEndpointConventionBuilder MapGpsdMetrics(this IEndpointRouteBuilder endpoints, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var scrape = endpoints.ServiceProvider.GetRequiredService<ScrapeSnapshot>();

        return endpoints.MapPrometheusScrapingEndpoint(
            path,
            meterProvider: null,
            configureBranchedPipeline: pipeline => pipeline.Use(async (context, next) =>
            {
                using var pin = await scrape.PinAsync(context.RequestAborted).ConfigureAwait(false);
                await next(context).ConfigureAwait(false);
            }),
            optionsName: null);
    }
}
