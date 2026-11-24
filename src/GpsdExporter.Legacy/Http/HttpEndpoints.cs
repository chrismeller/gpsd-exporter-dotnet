using GpsdExporter.Metrics;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace GpsdExporter.Http;

/// <summary>The exporter's HTTP surface: <c>/metrics</c>, <c>/healthz</c> and Kestrel's limits (R5).</summary>
internal static class HttpEndpoints
{
    public const string HealthPath = "/healthz";

    public static readonly TimeSpan RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(30);
    public const int MaxConcurrentConnections = 64;

    private static readonly string[] GetOnly = [HttpMethods.Get];

    /// <summary>
    /// Maps the scrape endpoint and the health check, both <c>GET</c> only: another method gets
    /// 405 from routing, and any other path 404 (AC5.4, AC5.7).
    /// </summary>
    public static void MapExporterEndpoints(this IEndpointRouteBuilder endpoints, string metricsPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGpsdMetrics(metricsPath).WithMetadata(new HttpMethodMetadata(GetOnly));

        // Healthy while the process runs, whatever gpsd is doing: gpsd being down is
        // gpsd_up 0 on a successful scrape, not a failed check (AC5.4).
        RequestDelegate healthy = static context =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync("ok\n", context.RequestAborted);
        };
        endpoints.MapGet(HealthPath, healthy);
    }

    /// <summary>The limits the Go exporter lacked (AC5.7). Nothing the exporter serves takes a body.</summary>
    public static void ApplyLimits(KestrelServerLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.RequestHeadersTimeout = RequestHeadersTimeout;
        limits.KeepAliveTimeout = KeepAliveTimeout;
        limits.MaxConcurrentConnections = MaxConcurrentConnections;
        limits.MaxConcurrentUpgradedConnections = 0;
        limits.MaxRequestBodySize = 0;
    }
}
