using GpsdExporter.Configuration;
using GpsdExporter.Connection;
using GpsdExporter.State;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace GpsdExporter.Metrics;

internal static class MetricsEndpoints
{
    /// <summary>
    /// What <c>/metrics</c> needs: <c>Metrics</c> options, the scrape duration histogram, and
    /// Kestrel's limits. The scrape endpoint is all the exporter serves to the outside, and
    /// nothing it serves takes a body.
    /// </summary>
    public static TBuilder AddGpsdMetrics<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<MetricsOptions>().BindConfiguration(MetricsOptions.Section);
        builder.Services.AddSingleton<ScrapeDurations>();
        builder.Services.Configure<KestrelServerOptions>(kestrel =>
        {
            kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            kestrel.Limits.MaxConcurrentConnections = 64;
            kestrel.Limits.MaxConcurrentUpgradedConnections = 0;
            kestrel.Limits.MaxRequestBodySize = 0;
        });
        return builder;
    }

    /// <summary>
    /// Maps <c>GET</c> on <c>Http:MetricsPath</c>. Each scrape takes one
    /// <see cref="StateSnapshot"/>, so every gpsd series in it comes from the same state and the
    /// same "now", and records how long it took in <c>gpsd_exporter_scrape_duration_seconds</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapGpsdMetrics(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var path = endpoints.ServiceProvider.GetRequiredService<IOptions<HttpOptions>>().Value.MetricsPath;
        return endpoints.MapGet(path, Scrape);
    }

    private static async Task Scrape(
        HttpContext context,
        StateStore store,
        ConnectionStats stats,
        ScrapeDurations durations,
        IOptions<MetricsOptions> options,
        TimeProvider time)
    {
        var started = time.GetTimestamp();
        var text = PrometheusWriter.Write(store.Snapshot(), stats, durations, options.Value);

        context.Response.ContentType = PrometheusWriter.ContentType;
        await context.Response.WriteAsync(text, context.RequestAborted).ConfigureAwait(false);
        durations.Record(time.GetElapsedTime(started));
    }
}
