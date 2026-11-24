using GpsdExporter.Metrics;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Metrics.Exposition;

/// <summary>The scrape as a whole: golden snapshot and naming (R7).</summary>
[Collection(SerialHostTests.Name)]
public sealed class CatalogueExpositionTests
{
    [Fact]
    public async Task T7_2_full_scrape_matches_the_approved_snapshot()
    {
        await using var session = await GoldenSession.StartAsync();

        // The new host's golden test (GpsdExporter.Tests.Host) writes the scrape for promtool.
        var text = await session.Host.ScrapeTextAsync();

        await Verify(PrometheusSnapshot.Normalize(text));
    }

    [Fact]
    public async Task T7_3_no_scope_labels_target_info_or_double_suffixes()
    {
        await using var session = await GoldenSession.StartAsync();

        var scrape = await session.Host.ScrapeAsync();

        scrape.Samples.ShouldNotBeEmpty();
        scrape.Samples.ShouldNotContain(s => s.Labels.Keys.Any(k => k.StartsWith("otel_", StringComparison.Ordinal)));
        scrape.Family("target_info").ShouldBeNull();
        scrape.Samples.ShouldNotContain(s => s.Name == "target_info");

        // Every exposed family is a catalogue metric, with the catalogue's exact name, type and help.
        var catalogue = MetricCatalogue.All.ToDictionary(m => m.Name, StringComparer.Ordinal);
        foreach (var family in scrape.Families)
        {
            catalogue.ShouldContainKey(family.Name);
            var metric = catalogue[family.Name];
            family.Type.ShouldBe(metric.Kind.ToString().ToLowerInvariant(), family.Name);
            family.Help.ShouldBe(metric.Help, family.Name);
            string[] names = metric.Kind == MetricKind.Histogram
                ? [family.Name + "_bucket", family.Name + "_sum", family.Name + "_count"]
                : [family.Name];
            family.Samples.ShouldAllBe(s => names.Contains(s.Name), family.Name);
        }
    }

    [Fact]
    public async Task Every_series_has_exactly_the_labels_its_catalogue_entry_allows()
    {
        await using var session = await GoldenSession.StartAsync();
        await session.Host.SendAndWaitAsync(session.Connection, Fixtures.LoadBytes("session-multi-gnss.jsonl"));

        var scrape = await session.Host.ScrapeAsync();

        string[] withoutDevice = ["gpsd_up", "gpsd_version_info"];
        foreach (var family in scrape.Families)
        {
            var metric = MetricCatalogue.All.Single(m => m.Name == family.Name);
            foreach (var sample in family.Samples)
            {
                var allowed = sample.Name.EndsWith("_bucket", StringComparison.Ordinal) ? metric.Labels.Add("le") : metric.Labels;
                sample.Labels.Keys.ShouldAllBe(k => allowed.Contains(k), sample.Series);
                if (!family.Name.StartsWith("gpsd_exporter_", StringComparison.Ordinal) && !withoutDevice.Contains(family.Name))
                {
                    sample.Labels.ShouldContainKey("device", sample.Series);
                }
            }
        }

        scrape.DuplicateSeries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Golden_session_exposes_every_metric_in_the_catalogue()
    {
        await using var session = await GoldenSession.StartAsync();

        var scrape = await session.Host.ScrapeAsync();

        scrape.Families.Select(f => f.Name).ShouldBe(MetricCatalogue.All.Select(m => m.Name), ignoreOrder: true);
    }

    /// <summary>
    /// T7.2's session: the real connection manager, connected to a FakeGpsd, receives a full
    /// multi-device session. <c>/dev/ttyACM0</c> has the complete 3D-fix session (every TPV,
    /// SKY and satellite field) and the timing session's PPS, TOFF and OSC; <c>/dev/ttyUSB0</c>
    /// has the two-device session's 2D fix and sky view, plus a DEVICE object with <c>bps</c>
    /// (no session fixture has one). The timing session's last TOFF, which has no precision,
    /// is left out so every metric in the catalogue appears. One scrape is made first so the
    /// scrape duration histogram has a sample.
    /// </summary>
    private sealed class GoldenSession : IAsyncDisposable
    {
        private static readonly string[] DataClasses = ["\"class\":\"TPV\"", "\"class\":\"SKY\"", "\"class\":\"PPS\"", "\"class\":\"TOFF\"", "\"class\":\"OSC\""];

        private const string UsbDevice =
            """{"class":"DEVICE","path":"/dev/ttyUSB0","driver":"u-blox","subtype":"SW ROM CORE 3.01 (107888),HW 00080000","activated":"2026-11-23T09:59:58.000Z","native":1,"bps":115200,"cycle":1.0}""";

        private readonly FakeGpsd _gpsd;

        private GoldenSession(FakeGpsd gpsd, MetricsHost host, FakeGpsdConnection connection)
        {
            _gpsd = gpsd;
            Host = host;
            Connection = connection;
        }

        public MetricsHost Host { get; }

        public FakeGpsdConnection Connection { get; }

        public static async Task<GoldenSession> StartAsync()
        {
            var gpsd = FakeGpsd.CreateStarted();
            var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
            var session = new GoldenSession(gpsd, host, connection);

            IEnumerable<string> lines =
            [
                .. Fixtures.Load("session-3d-fix.jsonl"),
                .. DataLines("session-timing.jsonl").Where(l => !l.Contains("\"class\":\"TOFF\"", StringComparison.Ordinal) || l.Contains("\"precision\"", StringComparison.Ordinal)),
                .. DataLines("session-two-devices.jsonl").Where(l => l.Contains("\"device\":\"/dev/ttyUSB0\"", StringComparison.Ordinal)),
                UsbDevice,
            ];
            await host.SendAndWaitAsync(connection, lines);
            await host.ScrapeTextAsync();
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await _gpsd.DisposeAsync();
        }

        private static IEnumerable<string> DataLines(string fixture) =>
            Fixtures.Load(fixture).Where(l => DataClasses.Any(c => l.Contains(c, StringComparison.Ordinal)));
    }
}
