using GpsdExporter.Protocol;
using GpsdExporter.Testing;

namespace GpsdExporter.Tests.Metrics.Exposition;

/// <summary>The exporter's own health metrics and the daemon and device series (R5).</summary>
[Collection(SerialHostTests.Name)]
public sealed class ExporterMetricsTests
{
    private const string Acm0 = "/dev/ttyACM0";

    private static readonly string[] AttemptResults = ["success", "refused", "timeout", "error"];
    private static readonly string[] DisconnectReasons = ["closed", "error", "keepalive_timeout", "shutdown"];

    [Fact]
    public async Task T5_1_at_startup_with_gpsd_down_up_is_0_and_build_info_has_the_assembly_version()
    {
        await using var gpsd = new FakeGpsd();
        await using var host = new MetricsHost(gpsdAddress: gpsd.Address);
        await MetricsHost.WaitUntil(() => host.Stats.TotalAttempts >= 1, "the first attempt fails");

        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_up").ShouldBe(0);
        var build = scrape.All("gpsd_exporter_build_info").ShouldHaveSingleItem();
        build.Value.ShouldBe(1);
        build.Labels["version"].ShouldBe(BuildInfo.Version);
        build.Labels["version"].ShouldStartWith(typeof(BuildInfo).Assembly.GetName().Version!.ToString(2));
        build.Labels["revision"].ShouldNotBeNullOrEmpty();
        build.Labels["runtime"].ShouldStartWith(".NET ");
        scrape.Value("gpsd_exporter_connection_attempts_total", ("result", "refused")).ShouldBeGreaterThanOrEqualTo(1);
        scrape.Value("gpsd_exporter_reconnect_delay_seconds").ShouldBeGreaterThan(0);
        scrape.All("gpsd_exporter_connected_timestamp_seconds").ShouldBeEmpty();
        scrape.All("gpsd_exporter_last_message_timestamp_seconds").ShouldBeEmpty();
        GpsdExpositionTests.DerivedNames(scrape).ShouldBeEmpty();
    }

    [Fact]
    public async Task AC5_3_every_counter_label_value_is_present_from_startup_at_0()
    {
        await using var host = new MetricsHost();

        var scrape = await host.ScrapeAsync();

        foreach (var result in AttemptResults)
        {
            scrape.Value("gpsd_exporter_connection_attempts_total", ("result", result)).ShouldBe(0);
        }

        foreach (var reason in DisconnectReasons)
        {
            scrape.Value("gpsd_exporter_disconnects_total", ("reason", reason)).ShouldBe(0);
        }

        foreach (var cls in GpsdClassExtensions.All)
        {
            scrape.Value("gpsd_exporter_messages_total", ("class", cls.Label())).ShouldBe(0);
        }

        foreach (var reason in ParseErrorReasonExtensions.All)
        {
            scrape.Value("gpsd_exporter_parse_errors_total", ("reason", reason.Label())).ShouldBe(0);
        }

        scrape.All("gpsd_exporter_connection_attempts_total").Count.ShouldBe(AttemptResults.Length);
        scrape.All("gpsd_exporter_messages_total").Count.ShouldBe(GpsdClassExtensions.All.Count);
        scrape.All("gpsd_exporter_parse_errors_total").Count.ShouldBe(ParseErrorReasonExtensions.All.Count);
        scrape.Value("gpsd_up").ShouldBe(0);
        scrape.Value("gpsd_exporter_reconnect_delay_seconds").ShouldBe(0);
    }

    [Fact]
    public async Task T5_3_parse_error_counters_match_the_bad_lines_sent()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _host = host;

        await host.SendAndWaitAsync(connection, Fixtures.LoadBytes("bad-input.jsonl"));
        var scrape = await host.ScrapeAsync();

        // The counts listed in the fixture's header.
        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "invalid_json")).ShouldBe(5);
        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "not_object")).ShouldBe(4);
        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "missing_class")).ShouldBe(3);
        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "bad_field")).ShouldBe(3);
        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "missing_field")).ShouldBe(2);
        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "too_long")).ShouldBe(1);
        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "missing_device")).ShouldBe(0);
        scrape.Value("gpsd_exporter_messages_total", ("class", "other")).ShouldBe(2);
        scrape.Value("gpsd_exporter_messages_total", ("class", "ERROR")).ShouldBe(1);
        scrape.Value("gpsd_up").ShouldBe(1);
        scrape.Has("gpsd_exporter_last_message_timestamp_seconds").ShouldBeTrue();
    }

    [Fact]
    public async Task Too_many_devices_is_counted()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _host = host;

        await host.SendAndWaitAsync(connection, Enumerable.Range(0, 17).Select(i => $$"""{"class":"TPV","device":"/dev/gps{{i}}","mode":1}"""));
        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "too_many_devices")).ShouldBe(1);
        scrape.All("gpsd_tpv_mode").Count.ShouldBe(16);
        scrape.All("gpsd_device_info").Count.ShouldBe(16);
    }

    [Fact]
    public async Task Connected_session_reports_version_devices_and_connection_figures()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _host = host;

        await host.SendAndWaitAsync(connection, Fixtures.LoadBytes("session-3d-fix.jsonl"));
        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_up").ShouldBe(1);
        scrape.Value("gpsd_exporter_connection_attempts_total", ("result", "success")).ShouldBe(1);
        scrape.Value("gpsd_exporter_reconnect_delay_seconds").ShouldBe(0);
        scrape.Value("gpsd_exporter_connected_timestamp_seconds").ShouldBe(host.Time.GetUtcNow().ToUnixTimeSeconds());
        scrape.Value("gpsd_exporter_messages_total", ("class", "TPV")).ShouldBe(10);
        scrape.Value("gpsd_exporter_messages_total", ("class", "SKY")).ShouldBe(10);

        var version = scrape.All("gpsd_version_info").ShouldHaveSingleItem();
        version.Labels.Keys.Order(StringComparer.Ordinal).ShouldBe(["proto_major", "proto_minor", "release", "rev"]);

        var device = scrape.All("gpsd_device_info").ShouldHaveSingleItem();
        device.Labels["device"].ShouldBe(Acm0);
        device.Labels["driver"].ShouldBe("u-blox");
        device.Labels.ShouldContainKey("subtype");
        scrape.Value("gpsd_device_cycle_seconds", ("device", Acm0)).ShouldBe(1);
        scrape.All("gpsd_device_bits_per_second").ShouldBeEmpty(); // The fixture's DEVICE has no bps.
        scrape.All("gpsd_device_last_report_timestamp_seconds").Select(s => s.Labels["class"]).Order(StringComparer.Ordinal)
            .ShouldBe(["DEVICE", "SKY", "TPV"]);
    }

    [Fact]
    public async Task Disconnect_drops_up_and_every_gpsd_derived_series_and_counts_the_reason()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _host = host;
        await host.SendAndWaitAsync(connection, Fixtures.LoadBytes("session-3d-fix.jsonl"));

        await connection.CloseAsync();
        await MetricsHost.WaitUntil(() => !host.Stats.Up, "the disconnect is noticed");
        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_up").ShouldBe(0);
        scrape.Value("gpsd_exporter_disconnects_total", ("reason", "closed")).ShouldBe(1);
        scrape.All("gpsd_version_info").ShouldBeEmpty();
        scrape.All("gpsd_exporter_connected_timestamp_seconds").ShouldBeEmpty();
        GpsdExpositionTests.DerivedNames(scrape).ShouldBeEmpty();
    }

    [Fact]
    public async Task Scrape_duration_is_recorded_for_each_scrape()
    {
        await using var host = new MetricsHost();

        await host.ScrapeTextAsync();
        await host.ScrapeTextAsync();
        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_exporter_scrape_duration_seconds_count").ShouldBe(2);
        scrape.Has("gpsd_exporter_scrape_duration_seconds_bucket", ("le", "0.001")).ShouldBeTrue();
        scrape.Has("gpsd_exporter_scrape_duration_seconds_bucket", ("le", "+Inf")).ShouldBeTrue();
    }
}
