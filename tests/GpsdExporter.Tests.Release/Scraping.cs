using System.Net;
using System.Net.Sockets;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Release;

/// <summary>Scrapes a running exporter over HTTP until a condition holds.</summary>
internal static class Scraping
{
    /// <summary>
    /// Scrapes <c>/metrics</c> every 50 ms until <paramref name="condition"/> holds, tolerating
    /// connection failures while the exporter starts. On timeout, the message has the last scrape.
    /// </summary>
    public static async Task<PrometheusText> Until(HttpClient http, Func<PrometheusText, bool> condition, string because, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + PublishedBinary.Limit;
        var last = "(no scrape)";
        while (true)
        {
            try
            {
                last = await http.GetStringAsync(new Uri("/metrics", UriKind.Relative), cancellationToken);
                var scrape = PrometheusText.Parse(last);
                if (condition(scrape))
                {
                    return scrape;
                }
            }
            catch (HttpRequestException) when (DateTime.UtcNow < deadline)
            {
                // Not listening yet.
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting until {because}. Last scrape:\n{last}");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
