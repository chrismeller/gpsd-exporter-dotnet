using System.Net;
using GpsdExporter.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GpsdExporter.Tests.Host.Metrics;

/// <summary>The <c>/metrics</c> endpoint over HTTP: methods, paths, content type, format and Kestrel's limits.</summary>
public sealed class MetricsEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Sessions => new(
        "session-3d-fix.jsonl",
        "session-partial-epochs.jsonl",
        "session-fix-loss.jsonl",
        "session-sky-shrink.jsonl",
        "session-multi-gnss.jsonl",
        "session-two-devices.jsonl",
        "session-timing.jsonl");

    [Fact]
    public async Task Scrape_is_Prometheus_text_format_0_0_4()
    {
        await using var host = new MetricsHost();

        using var response = await host.Client.GetAsync(new Uri("/metrics", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().ShouldBe("text/plain; version=0.0.4; charset=utf-8");
    }

    [Fact]
    public async Task Each_family_has_HELP_then_TYPE_and_no_sample_has_a_timestamp()
    {
        await using var host = new MetricsHost();
        host.Replay("session-3d-fix.jsonl");
        host.Replay("session-timing.jsonl");
        await host.ScrapeTextAsync();

        var text = await host.ScrapeTextAsync();

        text.ShouldEndWith("\n");
        var lines = text.TrimEnd('\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("# HELP ", StringComparison.Ordinal))
            {
                var name = lines[i].Split(' ')[2];
                lines[i + 1].ShouldStartWith($"# TYPE {name} ");
            }
        }

        var scrape = Testing.Prometheus.PrometheusText.Parse(text);
        scrape.Samples.ShouldAllBe(s => s.TimestampMs == null);
        scrape.Families.ShouldAllBe(f => f.Help != null && f.Type != null);
        scrape.Families.Select(f => f.Name).ShouldBeUnique();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public async Task Methods_other_than_GET_get_405(string method)
    {
        await using var host = new MetricsHost();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/metrics", UriKind.Relative));
        using var response = await host.Client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/metrics/extra")]
    public async Task Other_paths_get_404(string path)
    {
        await using var host = new MetricsHost();

        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Http_MetricsPath_moves_the_endpoint()
    {
        await using var host = new MetricsHost(settings: new Dictionary<string, string?> { ["Http:MetricsPath"] = "/gps/metrics" });

        var scrape = await host.ScrapeAsync();
        using var old = await host.Client.GetAsync(new Uri("/metrics", UriKind.Relative), Ct);

        scrape.Value("gpsd_up").ShouldBe(0);
        old.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Kestrel_limits_are_set()
    {
        await using var host = new MetricsHost();

        var limits = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;

        limits.RequestHeadersTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        limits.KeepAliveTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        limits.MaxConcurrentConnections.ShouldBe(64);
        limits.MaxConcurrentUpgradedConnections.ShouldBe(0);
        limits.MaxRequestBodySize.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Sessions))]
    public async Task Every_session_replays_into_a_valid_scrape(string fixture)
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _ = host;

        await host.SendAndWaitAsync(connection, Fixtures.LoadBytes(fixture));
        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_up").ShouldBe(1);
        scrape.DuplicateSeries().ShouldBeEmpty();
        scrape.NamesStartingWith("gpsd_tpv_").Concat(scrape.NamesStartingWith("gpsd_sky_")).Concat(scrape.NamesStartingWith("gpsd_pps_")).ShouldNotBeEmpty();

        // FakeGpsd's own VERSION, every fixture line, and the marker WATCH.
        scrape.All("gpsd_exporter_messages_total").Sum(s => s.Value).ShouldBe(Fixtures.Load(fixture).Count + 2);
    }
}
