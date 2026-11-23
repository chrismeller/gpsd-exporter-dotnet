using System.Diagnostics;
using System.Text;
using GpsdExporter.Connection;
using GpsdExporter.Protocol;
using GpsdExporter.Testing;
using Microsoft.Extensions.Logging;

namespace GpsdExporter.Tests.Integration.Connection;

public sealed class GpsdConnectionManagerTests
{
    private const string Watch = """?WATCH={"enable":true,"json":true,"pps":true};""";
    private const string Unwatch = """?WATCH={"enable":false};""";
    private const string Tpv = """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-11-23T10:00:00.000Z","lat":51.5,"lon":-0.1}""";

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<FakeGpsdConnection> ConnectedAsync(FakeGpsd gpsd, ConnectionHarness harness)
    {
        await harness.StartAsync();
        var connection = await gpsd.AcceptAsync(Ct);
        await connection.WaitForLineAsync(Watch, Ct);
        await ConnectionHarness.WaitUntil(() => harness.Stats.Up, "VERSION is processed");
        return connection;
    }

    [Fact]
    public async Task T1_1_gpsd_not_listening_counts_refused_attempts_and_keeps_going()
    {
        await using var gpsd = new FakeGpsd();
        await using var harness = new ConnectionHarness(gpsd.Address);

        await harness.StartAsync();
        await ConnectionHarness.WaitUntil(() => harness.Stats.Attempts(ConnectResult.Refused) == 1, "the first attempt is refused");

        harness.Stats.Up.ShouldBeFalse();
        harness.Stats.ConnectedAt.ShouldBeNull();
        harness.Stats.ReconnectDelay.TotalSeconds.ShouldBeInRange(0.8, 1.2);
        harness.Manager.ExecuteTask!.IsCompleted.ShouldBeFalse();

        await harness.AdvanceUntil(() => harness.Stats.Attempts(ConnectResult.Refused) == 2, Second, TimeSpan.FromSeconds(2), "a second attempt");
        harness.Manager.ExecuteTask!.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task T1_2_connects_once_gpsd_starts_after_three_failed_attempts()
    {
        await using var gpsd = new FakeGpsd();
        await using var harness = new ConnectionHarness(gpsd.Address);
        await harness.StartAsync();
        await harness.AdvanceUntil(() => harness.Stats.Attempts(ConnectResult.Refused) == 3, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10), "three refused attempts");

        gpsd.Start();
        await harness.AdvanceUntil(() => gpsd.Connections.Count == 1, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10), "a connection");
        await ConnectionHarness.WaitUntil(() => harness.Stats.Up, "gpsd_up after VERSION");

        harness.Stats.Attempts(ConnectResult.Success).ShouldBe(1);
        harness.Stats.ReconnectDelay.ShouldBe(TimeSpan.Zero);
        harness.Stats.ConnectedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task T1_3_sends_exactly_one_watch_line()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);

        var connection = await ConnectedAsync(gpsd, harness);
        await ConnectionHarness.Settle();

        connection.ReceivedText.ShouldBe(Watch + "\n");
    }

    [Fact]
    public async Task T1_3_sends_the_configured_device()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address, o => o with { Device = "/dev/ttyACM0" });

        await harness.StartAsync();
        var connection = await gpsd.AcceptAsync(Ct);

        await connection.WaitForLineAsync("""?WATCH={"enable":true,"json":true,"pps":true,"device":"/dev/ttyACM0"};""", Ct);
    }

    [Fact]
    public async Task T5_2_gpsd_up_changes_to_1_only_after_version()
    {
        await using var gpsd = FakeGpsd.CreateStarted(new FakeGpsdOptions { SendVersionOnConnect = false });
        await using var harness = new ConnectionHarness(gpsd.Address);

        await harness.StartAsync();
        var connection = await gpsd.AcceptAsync(Ct);
        await connection.WaitForLineAsync(Watch, Ct);
        await connection.SendLineAsync("""{"class":"DEVICES","devices":[]}""", Ct);
        await ConnectionHarness.WaitUntil(() => harness.Stats.Messages(GpsdClass.Devices) == 1, "DEVICES is processed");

        harness.Stats.ConnectedAt.ShouldNotBeNull();
        harness.Stats.Up.ShouldBeFalse();

        await connection.SendLineAsync(FakeGpsd.DefaultVersionLine, Ct);
        await ConnectionHarness.WaitUntil(() => harness.Stats.Up, "gpsd_up after VERSION");
        harness.Store.Current.Version.ShouldNotBeNull();
    }

    [Fact]
    public async Task Reports_reach_the_state_store_and_are_counted()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        await connection.SendLinesAsync([Tpv, "not json", """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"lat":"x"}""", """{"class":"AIS"}"""], Ct);
        await ConnectionHarness.WaitUntil(() => harness.Stats.Messages(GpsdClass.Other) == 1, "every line is processed");

        harness.Store.Current.Devices["/dev/ttyACM0"].Tpv![TpvField.Lat]!.Value.Value.ShouldBe(51.5);
        harness.Stats.Messages(GpsdClass.Version).ShouldBe(1);
        harness.Stats.Messages(GpsdClass.Tpv).ShouldBe(2);
        harness.Stats.ParseErrors(ParseErrorReason.InvalidJson).ShouldBe(1);
        harness.Stats.ParseErrors(ParseErrorReason.BadField).ShouldBe(1);
        harness.Stats.LastMessageAt.ShouldNotBeNull();
        harness.Logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Device added: /dev/ttyACM0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Store_level_skips_are_counted_as_parse_errors()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        await connection.SendLineAsync("""{"class":"TPV","mode":3}""", Ct);

        await ConnectionHarness.WaitUntil(() => harness.Stats.ParseErrors(ParseErrorReason.MissingDevice) == 1, "missing_device is counted");
    }

    [Fact]
    public async Task T2_8_a_line_over_64_KiB_is_counted_and_the_connection_kept()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        var huge = """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"pad":""" + "\"" + new string('x', 70 * 1024) + "\"}";
        await connection.SendLineAsync(huge, Ct);
        await connection.SendLineAsync(Tpv, Ct);

        await ConnectionHarness.WaitUntil(() => harness.Stats.Messages(GpsdClass.Tpv) == 1, "the TPV is processed");
        harness.Stats.ParseErrors(ParseErrorReason.TooLong).ShouldBe(1);
        harness.Store.Current.Devices["/dev/ttyACM0"].Tpv.ShouldNotBeNull();
        harness.Stats.Up.ShouldBeTrue();
        connection.IsClosed.ShouldBeFalse();
    }

    [Fact]
    public async Task T1_4_gpsd_closing_clears_state_counts_closed_and_reconnects()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);
        await connection.SendLineAsync(Tpv, Ct);
        await ConnectionHarness.WaitUntil(() => !harness.Store.Current.Devices.IsEmpty, "the TPV is applied");

        await connection.CloseAsync();

        await ConnectionHarness.WaitUntil(() => harness.Stats.Disconnects(DisconnectReason.Closed) == 1, "the disconnect is counted");
        harness.Stats.Up.ShouldBeFalse();
        harness.Stats.ConnectedAt.ShouldBeNull();
        harness.Store.Current.Devices.ShouldBeEmpty();
        harness.Store.Current.Version.ShouldBeNull();

        // The connection had delivered VERSION, so the backoff starts again from 1 s. The delay
        // is set just after the disconnect is counted, so wait for it.
        await ConnectionHarness.WaitUntil(() => harness.Stats.ReconnectDelay > TimeSpan.Zero, "the reconnect delay is set");
        harness.Stats.ReconnectDelay.TotalSeconds.ShouldBeInRange(0.8, 1.2);
        await harness.AdvanceUntil(() => gpsd.Connections.Count == 2, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2), "a reconnect");
        await ConnectionHarness.WaitUntil(() => harness.Stats.Up, "gpsd_up after the new VERSION");
    }

    [Fact]
    public async Task T1_5_a_partial_line_at_close_is_discarded()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        await connection.SendPartialLineAndCloseAsync(Tpv, Tpv.Length - 1, Ct);

        await ConnectionHarness.WaitUntil(() => harness.Stats.Disconnects(DisconnectReason.Closed) == 1, "the disconnect is counted");
        harness.Stats.Messages(GpsdClass.Tpv).ShouldBe(0);
        harness.Stats.ParseErrors(ParseErrorReason.InvalidJson).ShouldBe(0);
        harness.Store.Current.Devices.ShouldBeEmpty();
    }

    [Fact]
    public async Task T1_6_a_silent_gpsd_is_probed_after_15_s_and_dropped_5_s_later()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        harness.Time.Advance(TimeSpan.FromSeconds(14));
        await ConnectionHarness.Settle();
        connection.ReceivedLines.ShouldBe([Watch]);

        await harness.AdvanceUntil(() => connection.ReceivedLines.Contains("?VERSION;"), Second, TimeSpan.FromSeconds(2), "the keepalive probe");

        harness.Time.Advance(TimeSpan.FromSeconds(4));
        await ConnectionHarness.Settle();
        harness.Stats.Disconnects(DisconnectReason.KeepaliveTimeout).ShouldBe(0);
        harness.Stats.Up.ShouldBeTrue();

        await harness.AdvanceUntil(() => harness.Stats.Disconnects(DisconnectReason.KeepaliveTimeout) == 1, Second, TimeSpan.FromSeconds(2), "the keepalive timeout");
        await connection.WaitForClientCloseAsync(Ct);
        harness.Stats.Up.ShouldBeFalse();
    }

    [Fact]
    public async Task A_reply_to_the_keepalive_probe_keeps_the_connection()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        await harness.AdvanceUntil(() => connection.ReceivedLines.Contains("?VERSION;"), Second, TimeSpan.FromSeconds(16), "the keepalive probe");
        await connection.SendLineAsync(FakeGpsd.DefaultVersionLine, Ct);
        await ConnectionHarness.WaitUntil(() => harness.Stats.Messages(GpsdClass.Version) == 2, "the reply is processed");

        // The message is counted before the manager re-arms its idle timer; let it finish.
        await ConnectionHarness.Settle();
        harness.Time.Advance(TimeSpan.FromSeconds(10));
        await ConnectionHarness.Settle();

        harness.Stats.Disconnects(DisconnectReason.KeepaliveTimeout).ShouldBe(0);
        connection.IsClosed.ShouldBeFalse();
    }

    [Fact]
    public async Task Regular_data_never_triggers_the_keepalive()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        for (var i = 0; i < 30; i++)
        {
            harness.Time.Advance(Second);
            await connection.SendLineAsync(Tpv, Ct);
            await ConnectionHarness.WaitUntil(() => harness.Stats.Messages(GpsdClass.Tpv) == i + 1, "the TPV is processed");
        }

        connection.ReceivedLines.ShouldBe([Watch]);
        harness.Stats.Up.ShouldBeTrue();
    }

    [Fact]
    public async Task T1_7_after_gpsd_restarts_state_comes_only_from_new_data()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);
        await connection.SendLineAsync(Tpv, Ct);
        await ConnectionHarness.WaitUntil(() => !harness.Store.Current.Devices.IsEmpty, "the TPV is applied");

        await gpsd.RestartAsync();
        await ConnectionHarness.WaitUntil(() => !harness.Stats.Up, "the loss is noticed");
        harness.Store.Current.Devices.ShouldBeEmpty();

        await harness.AdvanceUntil(() => gpsd.Connections.Count == 2, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2), "a reconnect");
        var second = gpsd.Connections[1];
        await second.WaitForLineAsync(Watch, Ct);
        await ConnectionHarness.WaitUntil(() => harness.Stats.Up, "gpsd_up after the new VERSION");
        harness.Store.Current.Devices.ShouldBeEmpty();

        await second.SendLineAsync(Tpv.Replace("51.5", "40.7", StringComparison.Ordinal), Ct);
        await ConnectionHarness.WaitUntil(() => !harness.Store.Current.Devices.IsEmpty, "the new TPV is applied");
        harness.Store.Current.Devices["/dev/ttyACM0"].Tpv![TpvField.Lat]!.Value.Value.ShouldBe(40.7);
    }

    [Fact]
    public async Task T1_8_thirty_seconds_with_gpsd_down_follow_the_backoff_schedule()
    {
        await using var gpsd = new FakeGpsd();
        await using var harness = new ConnectionHarness(gpsd.Address);
        await harness.StartAsync();
        await ConnectionHarness.WaitUntil(() => harness.Stats.TotalAttempts == 1, "the first attempt");

        for (var elapsed = 0; elapsed < 120; elapsed++)
        {
            harness.Time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(10, Ct);
        }

        // Attempts at 0, then after 1, 2, 4, 8 and 16 s, each ±20%: 5 or 6 in 30 s.
        harness.Stats.TotalAttempts.ShouldBeInRange(4, 6);
        harness.Stats.Attempts(ConnectResult.Refused).ShouldBe(harness.Stats.TotalAttempts);
    }

    [Fact]
    public async Task T1_9_stopping_while_connected_unwatches_and_closes_quickly()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        var stopwatch = Stopwatch.StartNew();
        await harness.StopAsync();
        stopwatch.Stop();

        await connection.WaitForLineAsync(Unwatch, Ct);
        await connection.WaitForClientCloseAsync(Ct);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        harness.Stats.Disconnects(DisconnectReason.Shutdown).ShouldBe(1);
        harness.Manager.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Stopping_while_waiting_to_reconnect_is_immediate()
    {
        await using var gpsd = new FakeGpsd();
        await using var harness = new ConnectionHarness(gpsd.Address);
        await harness.StartAsync();
        await ConnectionHarness.WaitUntil(() => harness.Stats.TotalAttempts == 1, "the first attempt");

        var stopwatch = Stopwatch.StartNew();
        await harness.StopAsync();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        harness.Manager.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task T1_11_an_unexpected_protocol_version_warns_once_and_carries_on()
    {
        const string version4 = """{"class":"VERSION","release":"4.0","rev":"4.0","proto_major":4,"proto_minor":0}""";
        await using var gpsd = FakeGpsd.CreateStarted(new FakeGpsdOptions { VersionLine = version4 });
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        await connection.SendLineAsync(Tpv, Ct);
        await ConnectionHarness.WaitUntil(() => !harness.Store.Current.Devices.IsEmpty, "the TPV is applied");

        // A reconnect does not repeat the warning.
        await connection.CloseAsync();
        await harness.AdvanceUntil(() => gpsd.Connections.Count == 2, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2), "a reconnect");
        await ConnectionHarness.WaitUntil(() => harness.Stats.Messages(GpsdClass.Version) == 2, "the second VERSION");

        harness.Logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("protocol", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task A_gpsd_error_is_logged_at_warning_and_does_not_disconnect()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        await connection.SendLineAsync("""{"class":"ERROR","message":"Unrecognized request '?FOO'"}""", Ct);
        await ConnectionHarness.WaitUntil(() => harness.Stats.Messages(GpsdClass.Error) == 1, "the ERROR is processed");

        harness.Logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("?FOO", StringComparison.Ordinal));
        harness.Stats.Up.ShouldBeTrue();
        connection.IsClosed.ShouldBeFalse();
    }

    [Fact]
    public async Task An_aborted_connection_counts_as_an_error_or_close_and_reconnects()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        connection.Abort();

        await ConnectionHarness.WaitUntil(
            () => harness.Stats.Disconnects(DisconnectReason.Error) + harness.Stats.Disconnects(DisconnectReason.Closed) == 1,
            "the disconnect is counted");
        await harness.AdvanceUntil(() => gpsd.Connections.Count == 2, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2), "a reconnect");
    }

    [Fact]
    public void Invalid_options_are_rejected_at_construction()
    {
        var options = new GpsdOptions { Device = "/dev/" + new string('x', 100) };

        Should.Throw<ArgumentException>(() => new GpsdConnectionManager(
            options, new State.StateStore(TimeProvider.System), new ConnectionStats(), TimeProvider.System, new ListLogger<GpsdConnectionManager>()))
            .Message.ShouldContain("80");
    }

    [Fact]
    public async Task Only_one_connection_is_open_at_a_time()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        await using var harness = new ConnectionHarness(gpsd.Address);
        var connection = await ConnectedAsync(gpsd, harness);

        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await ConnectionHarness.Settle();

        gpsd.Connections.Count.ShouldBe(1);
        Encoding.UTF8.GetByteCount(connection.ReceivedText).ShouldBe(Watch.Length + 1);
    }
}
