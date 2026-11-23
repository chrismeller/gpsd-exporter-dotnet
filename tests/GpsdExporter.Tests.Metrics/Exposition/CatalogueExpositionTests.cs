using GpsdExporter.Metrics;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Metrics.Exposition;

/// <summary>The scrape as a whole: golden snapshot and naming (R7).</summary>
[Collection(SerialHostTests.Name)]
public sealed class CatalogueExpositionTests
{
    /// <summary>When set, the T7.2 test also writes the raw scrape to this path.</summary>
    public const string GoldenScrapeVariable = "GPSD_EXPORTER_GOLDEN_SCRAPE";

    private static readonly string[] DataClasses = ["\"class\":\"TPV\"", "\"class\":\"SKY\"", "\"class\":\"PPS\"", "\"class\":\"TOFF\"", "\"class\":\"OSC\""];

    /// <summary>
    /// T7.2: a full multi-device session. <c>/dev/ttyACM0</c> has the complete 3D-fix session
    /// (every TPV, SKY and satellite field) and the timing session's PPS, TOFF and OSC;
    /// <c>/dev/ttyUSB0</c> has the two-device session's 2D fix and sky view. The timing
    /// session's last TOFF, which has no precision, is left out so that every TPV, SKY,
    /// satellite and timing metric appears in the snapshot.
    /// </summary>
    internal static void ReplayGoldenSession(MetricsHost host)
    {
        host.Replay("session-3d-fix.jsonl");
        host.Apply(DataLines("session-timing.jsonl").Where(l => !l.Contains("\"class\":\"TOFF\"", StringComparison.Ordinal) || l.Contains("\"precision\"", StringComparison.Ordinal)));
        host.Apply(DataLines("session-two-devices.jsonl").Where(l => l.Contains("\"device\":\"/dev/ttyUSB0\"", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task T7_2_full_scrape_matches_the_approved_snapshot()
    {
        await using var host = new MetricsHost();
        ReplayGoldenSession(host);

        var text = await host.ScrapeTextAsync();

        // CI runs `promtool check metrics` on this exact scrape (T7.4).
        if (Environment.GetEnvironmentVariable(GoldenScrapeVariable) is { Length: > 0 } path)
        {
            await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        }

        await Verify(PrometheusSnapshot.Normalize(text));
    }

    [Fact]
    public async Task T7_3_no_scope_labels_target_info_or_double_suffixes()
    {
        await using var host = new MetricsHost();
        ReplayGoldenSession(host);

        var scrape = await host.ScrapeAsync();

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
            family.Samples.ShouldAllBe(s => s.Name == family.Name);
        }
    }

    [Fact]
    public async Task Every_series_has_exactly_the_labels_its_catalogue_entry_allows()
    {
        await using var host = new MetricsHost();
        ReplayGoldenSession(host);
        host.Replay("session-multi-gnss.jsonl");

        var scrape = await host.ScrapeAsync();

        var catalogue = MetricCatalogue.All.ToDictionary(m => m.Name, StringComparer.Ordinal);
        foreach (var sample in scrape.Samples)
        {
            var allowed = catalogue[sample.Name].Labels;
            sample.Labels.Keys.ShouldAllBe(k => allowed.Contains(k), sample.Series);
            sample.Labels.ShouldContainKey("device", sample.Series);
        }

        scrape.DuplicateSeries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Golden_session_exposes_every_gpsd_derived_metric_this_ticket_instruments()
    {
        await using var host = new MetricsHost();
        ReplayGoldenSession(host);

        var scrape = await host.ScrapeAsync();

        // Every TPV, SKY, satellite and timing metric appears, so the snapshot covers them all.
        var expected = MetricCatalogue.All
            .Select(m => m.Name)
            .Where(n => n.StartsWith("gpsd_tpv_", StringComparison.Ordinal) || n.StartsWith("gpsd_sky_", StringComparison.Ordinal)
                || n.StartsWith("gpsd_satellite_", StringComparison.Ordinal) || n.StartsWith("gpsd_pps_", StringComparison.Ordinal)
                || n.StartsWith("gpsd_toff_", StringComparison.Ordinal) || n.StartsWith("gpsd_osc_", StringComparison.Ordinal));
        scrape.Families.Select(f => f.Name).ShouldBe(expected, ignoreOrder: true);
    }

    private static IEnumerable<string> DataLines(string fixture) =>
        Fixtures.Load(fixture).Where(l => DataClasses.Any(c => l.Contains(c, StringComparison.Ordinal)));
}
