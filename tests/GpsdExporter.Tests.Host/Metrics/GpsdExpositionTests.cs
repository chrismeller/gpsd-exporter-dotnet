using GpsdExporter.Metrics;
using GpsdExporter.Protocol;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Host.Metrics;

/// <summary>The gpsd-derived series as scraped from <c>/metrics</c>.</summary>
public sealed class GpsdExpositionTests
{
    private const string Acm0 = "/dev/ttyACM0";
    private const string Usb0 = "/dev/ttyUSB0";

    /// <summary>Every gpsd-derived metric prefix; <c>gpsd_up</c>, <c>gpsd_version_info</c> and <c>gpsd_exporter_*</c> are not.</summary>
    internal static readonly string[] DerivedPrefixes = ["gpsd_device_", "gpsd_tpv_", "gpsd_sky_", "gpsd_satellite_", "gpsd_pps_", "gpsd_toff_", "gpsd_osc_"];

    private static readonly (string, string) Device = ("device", Acm0);

    [Fact]
    public async Task T3_1_man_page_TPV_example_produces_exactly_its_fields()
    {
        await using var host = new MetricsHost();
        var line = Fixtures.Load("doc-examples.jsonl").First(l => l.StartsWith("{\"class\":\"TPV\"", StringComparison.Ordinal));
        host.Apply(line);

        var scrape = await host.ScrapeAsync();

        var dev = ("device", "/dev/pts/1");
        scrape.Samples.Where(s => s.Name.StartsWith("gpsd_tpv_", StringComparison.Ordinal)).Select(s => s.Series).Order(StringComparer.Ordinal).ShouldBe(
        [
            Series("gpsd_tpv_climb_meters_per_second", dev),
            Series("gpsd_tpv_latitude_degrees", dev),
            Series("gpsd_tpv_longitude_degrees", dev),
            Series("gpsd_tpv_mode", dev),
            Series("gpsd_tpv_position_error_meters", dev, ("kind", "horizontal")),
            Series("gpsd_tpv_position_error_meters", dev, ("kind", "vertical")),
            Series("gpsd_tpv_speed_meters_per_second", dev),
            Series("gpsd_tpv_time_error_seconds", dev),
            Series("gpsd_tpv_timestamp_seconds", dev),
            Series("gpsd_tpv_track_degrees", dev),
        ]);
        scrape.Value("gpsd_tpv_latitude_degrees", dev).ShouldBe(46.498293369);
        scrape.Value("gpsd_tpv_mode", dev).ShouldBe(3);
        scrape.Value("gpsd_tpv_timestamp_seconds", dev).ShouldBe(1118226888.283, tolerance: 1e-6);
        scrape.Value("gpsd_tpv_position_error_meters", dev, ("kind", "vertical")).ShouldBe(32.321);
    }

    [Fact]
    public async Task T3_2_satellite_with_ss_0_has_an_snr_series_of_0()
    {
        await using var host = new MetricsHost();
        host.Apply("""{"class":"SKY","device":"/dev/ttyACM0","satellites":[{"PRN":19,"el":25,"az":177,"ss":0,"used":false}]}""");

        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_satellite_snr_dbhz", Device, ("prn", "19")).ShouldBe(0);
        scrape.Value("gpsd_satellite_used", Device, ("prn", "19")).ShouldBe(0);
    }

    [Fact]
    public async Task T3_3_VERSION_only_produces_only_up_version_and_exporter_metrics()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, _) = await MetricsHost.ConnectAsync(gpsd);
        await using var _host = host;

        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_up").ShouldBe(1);
        scrape.Family("gpsd_version_info")!.Samples.ShouldHaveSingleItem();
        DerivedNames(scrape).ShouldBeEmpty();
        scrape.Families.Select(f => f.Name)
            .Where(n => n is not ("gpsd_up" or "gpsd_version_info") && !n.StartsWith("gpsd_exporter_", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task T3_4_PPS_without_qErr_has_no_quantization_error_series()
    {
        await using var host = new MetricsHost();
        host.Apply("""{"class":"PPS","device":"/dev/ttyACM0","real_sec":1330212592,"real_nsec":343182000,"clock_sec":1330212592,"clock_nsec":343182002,"precision":-20}""");

        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_pps_offset_seconds", Device).ShouldBe(2e-9, tolerance: 1e-15);
        scrape.Value("gpsd_pps_precision_log2_seconds", Device).ShouldBe(-20);
        scrape.Value("gpsd_pps_pulses_total", Device).ShouldBe(1);
        scrape.Has("gpsd_pps_pulse_timestamp_seconds", Device).ShouldBeTrue();
        scrape.All("gpsd_pps_quantization_error_seconds").ShouldBeEmpty();
    }

    [Fact]
    public async Task T4_9_satellites_that_left_the_sky_are_absent_from_the_next_scrape()
    {
        await using var host = new MetricsHost();
        var lines = Fixtures.Load("session-sky-shrink.jsonl");
        var lastSky = lines.ToList().FindLastIndex(l => l.Contains("\"class\":\"SKY\"", StringComparison.Ordinal));
        host.Apply(lines.Take(lastSky));

        var before = await host.ScrapeAsync();
        host.Apply(lines.Skip(lastSky));
        var after = await host.ScrapeAsync();

        before.All("gpsd_satellite_snr_dbhz").Count.ShouldBe(3);
        after.All("gpsd_satellite_snr_dbhz").Count.ShouldBe(1);
        foreach (var name in after.NamesStartingWith("gpsd_satellite_"))
        {
            after.All(name).Count.ShouldBeLessThanOrEqualTo(1, name);
        }

        after.Value("gpsd_sky_satellites_visible", Device).ShouldBe(1);
    }

    [Fact]
    public async Task T4_10_disconnect_after_a_full_fix_leaves_no_gpsd_derived_series()
    {
        await using var host = new MetricsHost();
        host.Replay("session-3d-fix.jsonl");
        host.Replay("session-timing.jsonl");
        DerivedNames(await host.ScrapeAsync()).ShouldNotBeEmpty();

        // What the connection manager does when the connection is lost.
        host.Store.Reset();

        var scrape = await host.ScrapeAsync();
        DerivedNames(scrape).ShouldBeEmpty();
        scrape.All("gpsd_version_info").ShouldBeEmpty();
        scrape.Value("gpsd_up").ShouldBe(0);
    }

    [Fact]
    public async Task T4_11_TPV_series_expire_after_the_stale_after_time()
    {
        await using var host = new MetricsHost();
        host.Replay("session-3d-fix.jsonl");

        host.Time.Advance(TimeSpan.FromSeconds(10));
        var fresh = await host.ScrapeAsync();
        host.Time.Advance(TimeSpan.FromSeconds(1));
        var stale = await host.ScrapeAsync();

        fresh.NamesStartingWith("gpsd_tpv_").ShouldContain("gpsd_tpv_latitude_degrees");
        stale.NamesStartingWith("gpsd_tpv_").ShouldBeEmpty();

        // SKY's stale-after time is 30 s, so it is still served.
        stale.Has("gpsd_sky_satellites_visible", Device).ShouldBeTrue();
        stale.All("gpsd_satellite_used").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task T6_1_two_devices_produce_independent_series()
    {
        await using var host = new MetricsHost();
        var lines = Fixtures.Load("session-two-devices.jsonl");

        // Everything before the deactivation at the end.
        host.Apply(lines.TakeWhile(l => !l.Contains("\"activated\":0", StringComparison.Ordinal)));
        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_tpv_mode", ("device", Acm0)).ShouldBe(3);
        scrape.Value("gpsd_tpv_mode", ("device", Usb0)).ShouldBe(2);
        scrape.Value("gpsd_tpv_latitude_degrees", ("device", Usb0)).ShouldBe(40.689247);
        scrape.Value("gpsd_tpv_latitude_degrees", ("device", Acm0)).ShouldNotBe(40.689247);
        scrape.Has("gpsd_tpv_altitude_hae_meters", ("device", Acm0)).ShouldBeTrue();
        scrape.Has("gpsd_tpv_altitude_hae_meters", ("device", Usb0)).ShouldBeFalse();
        scrape.Value("gpsd_sky_satellites_visible", ("device", Usb0)).ShouldBe(2);
        scrape.DuplicateSeries().ShouldBeEmpty();

        host.Apply(lines.SkipWhile(l => !l.Contains("\"activated\":0", StringComparison.Ordinal)));
        var after = await host.ScrapeAsync();

        after.Samples.Where(s => s.Labels.GetValueOrDefault("device") == Usb0).ShouldBeEmpty();
        after.Has("gpsd_tpv_mode", ("device", Acm0)).ShouldBeTrue();
    }

    [Fact]
    public async Task T6_2_one_GPS_svid_on_two_signals_gives_two_series()
    {
        await using var host = new MetricsHost();
        host.Replay("session-multi-gnss.jsonl");

        var scrape = await host.ScrapeAsync();

        var gps3 = scrape.All("gpsd_satellite_snr_dbhz")
            .Where(s => s.Labels.GetValueOrDefault("gnssid") == "0" && s.Labels.GetValueOrDefault("svid") == "3")
            .ToList();
        gps3.Select(s => s.Labels["sigid"]).Order(StringComparer.Ordinal).ShouldBe(["0", "6"]);
        gps3.ShouldAllBe(s => s.Labels["constellation"] == "gps");
        scrape.DuplicateSeries().ShouldBeEmpty();
    }

    [Fact]
    public async Task T6_2_every_constellation_is_labelled()
    {
        await using var host = new MetricsHost();
        host.Replay("session-multi-gnss.jsonl");

        var scrape = await host.ScrapeAsync();

        scrape.All("gpsd_satellite_used").Select(s => s.Labels.GetValueOrDefault("constellation")).OfType<string>().Distinct().Order(StringComparer.Ordinal)
            .ShouldBe(["beidou", "galileo", "glonass", "gps", "imes", "navic", "qzss", "sbas"]);
    }

    [Fact]
    public async Task T6_3_satellite_with_only_a_PRN_is_labelled_by_prn_alone()
    {
        await using var host = new MetricsHost();
        host.Apply("""{"class":"SKY","device":"/dev/ttyACM0","satellites":[{"PRN":7,"el":13,"az":295,"ss":20,"used":true}]}""");

        var scrape = await host.ScrapeAsync();

        var series = scrape.All("gpsd_satellite_snr_dbhz").ShouldHaveSingleItem();
        series.Labels.Keys.Order(StringComparer.Ordinal).ShouldBe(["device", "prn"]);
        series.Labels["prn"].ShouldBe("7");
    }

    [Fact]
    public async Task T6_4_duplicate_satellite_identity_gives_one_series_and_is_counted()
    {
        await using var host = new MetricsHost();
        var results = host.Apply("""{"class":"SKY","device":"/dev/ttyACM0","satellites":[{"PRN":3,"gnssid":0,"svid":3,"sigid":0,"ss":30,"used":true},{"PRN":3,"gnssid":0,"svid":3,"sigid":0,"ss":35,"used":false}]}""");

        var scrape = await host.ScrapeAsync();

        results.ShouldHaveSingleItem().FieldErrors.ShouldContain(e => e.Reason == ParseErrorReason.DuplicateSatellite);
        scrape.All("gpsd_satellite_snr_dbhz").ShouldHaveSingleItem().Value.ShouldBe(35);
        scrape.DuplicateSeries().ShouldBeEmpty();
    }

    [Fact]
    public async Task T6_7_sixteen_devices_with_200_signals_each_are_all_exposed()
    {
        await using var host = new MetricsHost();
        for (var d = 0; d < 16; d++)
        {
            var satellites = Enumerable.Range(0, 200).Select(i =>
                $$"""{"PRN":{{i + 1}},"gnssid":{{i % 7}},"svid":{{(i / 7) + 1}},"sigid":{{i % 3}},"az":10,"el":20,"ss":30,"health":1,"qual":7,"prRes":0.5,"used":true}""");
            host.Apply($$"""{"class":"SKY","device":"/dev/gps{{d}}","satellites":[{{string.Join(',', satellites)}}]}""");
        }

        var scrape = await host.ScrapeAsync();

        foreach (var metric in MetricCatalogue.All.Where(m => m.Name.StartsWith("gpsd_satellite_", StringComparison.Ordinal)))
        {
            scrape.All(metric.Name).Count.ShouldBe(16 * 200, metric.Name);
        }

        scrape.DuplicateSeries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Turning_satellites_off_removes_only_per_satellite_series()
    {
        await using var host = new MetricsHost(settings: new Dictionary<string, string?> { ["Metrics:Satellites"] = "false" });
        host.Replay("session-3d-fix.jsonl");

        var scrape = await host.ScrapeAsync();

        scrape.NamesStartingWith("gpsd_satellite_").ShouldBeEmpty();
        scrape.Has("gpsd_sky_satellites_visible", Device).ShouldBeTrue();
        scrape.Has("gpsd_tpv_mode", Device).ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_scrapes_are_each_consistent()
    {
        await using var host = new MetricsHost();
        host.Replay("session-3d-fix.jsonl");

        var scrapes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => host.ScrapeAsync()));

        // The scrape duration histogram differs between scrapes; the gpsd series must not.
        scrapes.Select(s => string.Join('\n', s.Samples.Where(x => !x.Name.StartsWith("gpsd_exporter_scrape_", StringComparison.Ordinal))))
            .Distinct(StringComparer.Ordinal).ShouldHaveSingleItem();
    }

    internal static IReadOnlyList<string> DerivedNames(PrometheusText scrape) =>
        [.. DerivedPrefixes.SelectMany(scrape.NamesStartingWith)];

    private static string Series(string name, params (string Name, string Value)[] labels) =>
        new PrometheusSample(name, labels.ToDictionary(l => l.Name, l => l.Value, StringComparer.Ordinal), 0, null).Series;
}
