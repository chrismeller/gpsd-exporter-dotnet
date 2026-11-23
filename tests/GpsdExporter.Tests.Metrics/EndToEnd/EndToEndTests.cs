using System.Net;
using System.Text.Json;
using GpsdExporter.Connection;
using GpsdExporter.Protocol;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;
using GpsdExporter.Tests.Metrics.Exposition;

namespace GpsdExporter.Tests.Metrics.EndToEnd;

/// <summary>
/// The whole exporter (host, connection manager, state and metrics) against FakeGpsd, checked
/// through <c>/metrics</c> (EXT-899). Time is a <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/>,
/// so backoff and keepalive waits take no real time.
/// </summary>
[Collection(SerialHostTests.Name)]
public sealed class EndToEndTests
{
    private const string Device = "/dev/ttyACM0";
    private const string Watch = """?WATCH={"enable":true,"json":true,"pps":true};""";

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Sessions => new(
        "session-3d-fix.jsonl",
        "session-partial-epochs.jsonl",
        "session-fix-loss.jsonl",
        "session-sky-shrink.jsonl",
        "session-multi-gnss.jsonl",
        "session-two-devices.jsonl",
        "session-timing.jsonl");

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

    [Fact]
    public async Task T1_1_gpsd_not_listening_serves_gpsd_up_0_and_counts_refused_attempts()
    {
        await using var gpsd = new FakeGpsd();
        await using var host = new MetricsHost(gpsdAddress: gpsd.Address);
        await MetricsHost.WaitUntil(() => host.Stats.Attempts(ConnectResult.Refused) >= 1, "the first attempt is refused");
        await host.AdvanceUntil(() => host.Stats.Attempts(ConnectResult.Refused) >= 3, Step, TimeSpan.FromSeconds(10), "three refused attempts");

        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_up").ShouldBe(0);
        scrape.Value("gpsd_exporter_connection_attempts_total", ("result", "refused")).ShouldBeGreaterThanOrEqualTo(3);
        scrape.Value("gpsd_exporter_connection_attempts_total", ("result", "success")).ShouldBe(0);
        scrape.Value("gpsd_exporter_reconnect_delay_seconds").ShouldBeGreaterThan(0);
        GpsdSeries(scrape).ShouldBeEmpty();
        using var health = await host.Client.GetAsync(new Uri("/healthz", UriKind.Relative), Ct);
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T1_2_connects_and_serves_data_once_gpsd_starts()
    {
        await using var gpsd = new FakeGpsd();
        await using var host = new MetricsHost(gpsdAddress: gpsd.Address);
        await host.AdvanceUntil(() => host.Stats.Attempts(ConnectResult.Refused) >= 3, Step, TimeSpan.FromSeconds(10), "three refused attempts");

        gpsd.Start();
        await host.AdvanceUntil(() => gpsd.Connections.Count == 1, Step, TimeSpan.FromSeconds(20), "a connection");
        var connection = gpsd.Connections[0];
        await connection.WaitForLineAsync(Watch, Ct);
        await MetricsHost.WaitUntil(() => host.Stats.Up, "VERSION is processed");
        await host.SendAndWaitAsync(connection, Fixtures.LoadBytes("session-3d-fix.jsonl"));

        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_up").ShouldBe(1);
        scrape.Value("gpsd_exporter_connection_attempts_total", ("result", "success")).ShouldBe(1);
        scrape.Value("gpsd_exporter_reconnect_delay_seconds").ShouldBe(0);
        scrape.Has("gpsd_tpv_latitude_degrees", ("device", Device)).ShouldBeTrue();
    }

    [Fact]
    public async Task T1_4_gpsd_closing_the_socket_removes_gpsd_series_and_reconnects()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _ = host;
        await host.SendAndWaitAsync(connection, Fixtures.LoadBytes("session-3d-fix.jsonl"));
        var before = await host.ScrapeAsync();
        GpsdSeries(before).ShouldContain(n => n == "gpsd_version_info");
        GpsdSeries(before).ShouldContain(n => n == "gpsd_device_info");
        GpsdSeries(before).ShouldContain(n => n.StartsWith("gpsd_satellite_", StringComparison.Ordinal));

        await connection.CloseAsync();
        await MetricsHost.WaitUntil(() => host.Stats.Disconnects(DisconnectReason.Closed) == 1, "the close is noticed");
        var after = await host.ScrapeAsync();

        after.Value("gpsd_up").ShouldBe(0);
        GpsdSeries(after).ShouldBeEmpty();
        after.Value("gpsd_exporter_disconnects_total", ("reason", "closed")).ShouldBe(1);
        after.Value("gpsd_exporter_messages_total", ("class", "TPV")).ShouldBe(before.Value("gpsd_exporter_messages_total", ("class", "TPV")));
        after.Has("gpsd_exporter_connected_timestamp_seconds").ShouldBeFalse();

        await host.AdvanceUntil(() => gpsd.Connections.Count == 2, Step, TimeSpan.FromSeconds(5), "a reconnect");
        await gpsd.Connections[1].WaitForLineAsync(Watch, Ct);
        await MetricsHost.WaitUntil(() => host.Stats.Up, "the new VERSION");
        var reconnected = await host.ScrapeAsync();

        reconnected.Value("gpsd_up").ShouldBe(1);
        reconnected.Value("gpsd_exporter_connection_attempts_total", ("result", "success")).ShouldBe(2);
        reconnected.Has("gpsd_tpv_latitude_degrees", ("device", Device)).ShouldBeFalse();
    }

    [Fact]
    public async Task T1_6_a_stalled_gpsd_is_dropped_after_the_keepalive_and_the_scrape_says_so()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _ = host;
        await host.SendAndWaitAsync(connection, Fixtures.LoadBytes("session-3d-fix.jsonl"));

        // gpsd goes quiet: ?VERSION; after 15 s idle, then dead 5 s later.
        await host.AdvanceUntil(() => connection.ReceivedText.Contains("?VERSION;", StringComparison.Ordinal), Step, TimeSpan.FromSeconds(16), "the keepalive probe");
        await host.AdvanceUntil(() => host.Stats.Disconnects(DisconnectReason.KeepaliveTimeout) == 1, Step, TimeSpan.FromSeconds(6), "the keepalive timeout");
        var scrape = await host.ScrapeAsync();

        scrape.Value("gpsd_up").ShouldBe(0);
        scrape.Value("gpsd_exporter_disconnects_total", ("reason", "keepalive_timeout")).ShouldBe(1);
        GpsdSeries(scrape).ShouldBeEmpty();
    }

    [Fact]
    public async Task T1_7_after_gpsd_restarts_series_come_back_from_new_data_only()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _ = host;
        await host.SendAndWaitAsync(connection, Fixtures.LoadBytes("session-3d-fix.jsonl"));
        (await host.ScrapeAsync()).Has("gpsd_tpv_latitude_degrees", ("device", Device)).ShouldBeTrue();

        await gpsd.RestartAsync();
        await MetricsHost.WaitUntil(() => !host.Stats.Up, "the loss is noticed");
        GpsdSeries(await host.ScrapeAsync()).ShouldBeEmpty();

        await host.AdvanceUntil(() => gpsd.Connections.Count == 2, Step, TimeSpan.FromSeconds(5), "a reconnect");
        var second = gpsd.Connections[1];
        await second.WaitForLineAsync(Watch, Ct);
        await MetricsHost.WaitUntil(() => host.Stats.Up, "the new VERSION");
        var empty = await host.ScrapeAsync();
        empty.Value("gpsd_up").ShouldBe(1);
        empty.All("gpsd_tpv_latitude_degrees").ShouldBeEmpty();
        empty.All("gpsd_satellite_used").ShouldBeEmpty();

        await host.SendAndWaitAsync(second, [$$"""{"class":"TPV","device":"{{Device}}","mode":3,"time":"2026-11-23T10:00:00.000Z","lat":40.7,"lon":-74.0}"""]);
        var fresh = await host.ScrapeAsync();

        fresh.Value("gpsd_tpv_latitude_degrees", ("device", Device)).ShouldBe(40.7);
        fresh.All("gpsd_satellite_used").ShouldBeEmpty();
    }

    [Fact]
    public async Task T4_12_series_disappear_at_the_line_where_mode_drops()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _ = host;
        var lines = Fixtures.Load("session-fix-loss.jsonl");
        var preamble = lines.TakeWhile(l => !l.Contains("\"class\":\"TPV\"", StringComparison.Ordinal)).ToList();
        var tpvs = lines.Skip(preamble.Count).ToList();
        await host.SendAndWaitAsync(connection, preamble);

        var modes = new List<int>();
        foreach (var tpv in tpvs)
        {
            var mode = JsonDocument.Parse(tpv).RootElement.GetProperty("mode").GetInt32();
            modes.Add(mode);
            await host.SendAndWaitAsync(connection, [tpv]);
            var scrape = await host.ScrapeAsync();
            var at = $"after the TPV with mode {mode} (#{modes.Count})";

            scrape.Value("gpsd_tpv_mode", ("device", Device)).ShouldBe(mode, at);

            // Position, velocity, error estimates and the TPV timestamp need a fix (AC4.1).
            var fix = mode >= 2;
            scrape.Has("gpsd_tpv_latitude_degrees", ("device", Device)).ShouldBe(fix, at);
            scrape.Has("gpsd_tpv_longitude_degrees", ("device", Device)).ShouldBe(fix, at);
            scrape.Has("gpsd_tpv_speed_meters_per_second", ("device", Device)).ShouldBe(fix, at);
            scrape.Has("gpsd_tpv_track_degrees", ("device", Device)).ShouldBe(fix, at);
            scrape.Has("gpsd_tpv_timestamp_seconds", ("device", Device)).ShouldBe(fix, at);
            scrape.All("gpsd_tpv_position_error_meters").Any(s => s.Labels["device"] == Device).ShouldBe(fix, at);

            // Altitude, climb and vertical error need a 3D fix.
            var fix3d = mode == 3;
            scrape.Has("gpsd_tpv_altitude_hae_meters", ("device", Device)).ShouldBe(fix3d, at);
            scrape.Has("gpsd_tpv_climb_meters_per_second", ("device", Device)).ShouldBe(fix3d, at);
            scrape.All("gpsd_tpv_position_error_meters").Any(s => s.Labels.GetValueOrDefault("kind") == "vertical").ShouldBe(fix3d, at);
        }

        modes.ShouldBe([3, 3, 2, 1, 0, 1, 2, 3]);
    }

    [Fact]
    public async Task Two_devices_end_to_end_have_separate_series_until_one_is_deactivated()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _ = host;
        var lines = Fixtures.Load("session-two-devices.jsonl").ToList();
        var deactivation = lines.FindIndex(l => l.Contains("\"class\":\"DEVICE\"", StringComparison.Ordinal) && l.Contains("\"activated\":0", StringComparison.Ordinal));
        deactivation.ShouldBeGreaterThan(0);

        await host.SendAndWaitAsync(connection, lines.Take(deactivation));
        var both = await host.ScrapeAsync();

        both.Value("gpsd_tpv_mode", ("device", "/dev/ttyACM0")).ShouldBe(3);
        both.Value("gpsd_tpv_mode", ("device", "/dev/ttyUSB0")).ShouldBe(2);
        both.Has("gpsd_tpv_altitude_hae_meters", ("device", "/dev/ttyUSB0")).ShouldBeFalse();
        both.All("gpsd_satellite_used").Where(s => s.Labels.GetValueOrDefault("svid") == "3" && s.Labels.GetValueOrDefault("gnssid") == "0").Select(s => s.Labels["device"])
            .ShouldBe(["/dev/ttyACM0", "/dev/ttyUSB0"], ignoreOrder: true);

        await host.SendAndWaitAsync(connection, lines.Skip(deactivation));
        var one = await host.ScrapeAsync();

        one.Samples.ShouldNotContain(s => s.Labels.GetValueOrDefault("device") == "/dev/ttyUSB0");
        one.Has("gpsd_tpv_mode", ("device", "/dev/ttyACM0")).ShouldBeTrue();
        one.Value("gpsd_up").ShouldBe(1);
    }

    [Fact]
    public async Task Multi_GNSS_end_to_end_has_one_series_per_signal_and_drops_the_duplicate()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var (host, connection) = await MetricsHost.ConnectAsync(gpsd);
        await using var _ = host;
        var lines = Fixtures.Load("session-multi-gnss.jsonl");

        await host.SendAndWaitAsync(connection, lines);
        var scrape = await host.ScrapeAsync();

        // Expected identities from the last SKY: (gnssid, svid, sigid), or PRN alone.
        var sky = JsonDocument.Parse(lines.Last(l => l.Contains("\"class\":\"SKY\"", StringComparison.Ordinal))).RootElement;
        var identities = sky.GetProperty("satellites").EnumerateArray()
            .Select(s => s.TryGetProperty("gnssid", out var g) && s.TryGetProperty("svid", out var v)
                ? $"{g.GetInt32()}/{v.GetInt32()}/{(s.TryGetProperty("sigid", out var sig) ? sig.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture) : "")}"
                : $"prn {s.GetProperty("PRN").GetInt32()}")
            .ToList();
        var distinct = identities.Distinct(StringComparer.Ordinal).Count();
        distinct.ShouldBeLessThan(identities.Count); // The fixture has a duplicate identity.

        var used = scrape.All("gpsd_satellite_used");
        used.Count.ShouldBe(distinct);
        scrape.DuplicateSeries().ShouldBeEmpty();
        used.Count(s => s.Labels.GetValueOrDefault("gnssid") == "0" && s.Labels.GetValueOrDefault("svid") == "3").ShouldBe(2);
        used.Select(s => s.Labels.GetValueOrDefault("constellation")).OfType<string>().Distinct().Count().ShouldBeGreaterThan(1);
        used.ShouldContain(s => !s.Labels.ContainsKey("gnssid") && s.Labels.ContainsKey("prn"));
        scrape.Value("gpsd_exporter_parse_errors_total", ("reason", "duplicate_satellite")).ShouldBeGreaterThanOrEqualTo(1);
    }

    /// <summary>gpsd-derived series: everything except gpsd_up and the exporter's own.</summary>
    private static List<string> GpsdSeries(PrometheusText scrape) =>
        [.. scrape.Samples.Select(s => s.Name).Where(n => n != "gpsd_up" && !n.StartsWith("gpsd_exporter_", StringComparison.Ordinal)).Distinct()];
}
