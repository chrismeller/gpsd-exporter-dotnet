using System.Net;
using System.Net.Sockets;
using GpsdExporter.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GpsdExporter.Tests.Metrics.Exposition;

/// <summary>The HTTP surface in process: <c>/healthz</c>, methods, paths and Kestrel's limits (R5).</summary>
[Collection(SerialHostTests.Name)]
public sealed class HttpEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task T5_4_healthz_is_200_while_gpsd_is_down()
    {
        await using var host = new MetricsHost(gpsdAddress: $"127.0.0.1:{ClosedPort()}");
        await MetricsHost.WaitUntil(() => host.Stats.TotalAttempts > 0, "the exporter has tried gpsd");

        using var response = await host.Client.GetAsync(new Uri(HttpEndpoints.HealthPath, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("ok\n");
        host.Stats.Up.ShouldBeFalse();
        (await host.ScrapeAsync()).Value("gpsd_up").ShouldBe(0);
    }

    [Theory]
    [InlineData("/metrics")]
    [InlineData("/healthz")]
    public async Task T5_6_methods_other_than_GET_get_405(string path)
    {
        await using var host = new MetricsHost();

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
            using var response = await host.Client.SendAsync(request, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed, $"{method} {path}");
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/metrics/extra")]
    [InlineData("/health")]
    public async Task T5_6_other_paths_get_404(string path)
    {
        await using var host = new MetricsHost();

        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC5_7_Kestrel_limits_are_set()
    {
        await using var host = new MetricsHost();

        var limits = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;

        limits.RequestHeadersTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        limits.KeepAliveTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        limits.MaxConcurrentConnections.ShouldBe(64);
        limits.MaxRequestBodySize.ShouldBe(0);
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
