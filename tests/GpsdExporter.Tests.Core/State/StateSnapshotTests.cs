using System.Globalization;
using System.Text;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Core.State;

public sealed class StateSnapshotTests
{
    private const string Acm0 = "/dev/ttyACM0";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    private readonly StateStore _store;

    public StateSnapshotTests() => _store = new StateStore(_time);

    private void Apply(string line) => Apply(_store, line);

    private static void Apply(StateStore store, string line)
    {
        var parsed = GpsdParser.Parse(Encoding.UTF8.GetBytes(line));
        parsed.Error.ShouldBeNull(line);
        store.Apply(parsed.Report!).ShouldBeNull(line);
    }

    private static DeviceSnapshot Device(StateSnapshot snapshot) => snapshot.Devices.Single(d => d.Path == Acm0);

    private const string FullTpv =
        """{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-11-23T10:00:00.000Z","lat":51.5,"lon":-0.1,"altHAE":92.1,"ept":0.005}""";

    [Fact]
    public void tpv_is_served_up_to_its_stale_after_and_gone_after_it()
    {
        Apply("""{"class":"VERSION","release":"3.22","rev":"3.22","proto_major":3,"proto_minor":14}""");
        Apply("""{"class":"DEVICE","path":"/dev/ttyACM0","driver":"u-blox","activated":"2026-11-23T09:59:58.000Z"}""");
        Apply(FullTpv);

        _time.Advance(TimeSpan.FromSeconds(10));
        var atLimit = Device(_store.Snapshot());
        atLimit.TpvMode.ShouldBe(3);
        atLimit.TpvTimestamp.ShouldNotBeNull();
        atLimit.TpvFields.Count().ShouldBe(4);

        _time.Advance(TimeSpan.FromSeconds(1));
        var snapshot = _store.Snapshot();
        var device = Device(snapshot);
        device.TpvMode.ShouldBeNull();
        device.TpvTimestamp.ShouldBeNull();
        device.TpvFields.ShouldBeEmpty();
        device.Tpv(TpvField.Lat).ShouldBeNull();

        // The connection-level state is not subject to staleness.
        snapshot.Version.ShouldNotBeNull();
        device.Info.ShouldNotBeNull();
        device.LastReport.ShouldContainKey(GpsdClass.Tpv);
    }

    [Fact]
    public void A_fresh_report_brings_the_class_back()
    {
        Apply(FullTpv);
        _time.Advance(TimeSpan.FromSeconds(11));
        Device(_store.Snapshot()).TpvMode.ShouldBeNull();

        Apply("""{"class":"TPV","device":"/dev/ttyACM0","mode":2,"time":"2026-11-23T10:00:11.000Z","lat":51.6}""");

        var device = Device(_store.Snapshot());
        device.TpvMode.ShouldBe(2);
        device.Tpv(TpvField.Lat).ShouldBe(51.6);
    }

    [Fact]
    public void Each_value_ages_from_when_it_was_received()
    {
        Apply("""{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-11-23T10:00:00.000Z","lat":51.5}""");
        _time.Advance(TimeSpan.FromSeconds(9));
        Apply("""{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-11-23T10:00:09.000Z","speed":0.1}""");
        _time.Advance(TimeSpan.FromSeconds(2));

        // lat is still within the two-epoch rule, but was received 11 s ago.
        var device = Device(_store.Snapshot());
        device.TpvMode.ShouldBe(3);
        device.Tpv(TpvField.Speed).ShouldBe(0.1);
        device.Tpv(TpvField.Lat).ShouldBeNull();
        device.TpvFields.Select(f => f.Field).ShouldBe([TpvField.Speed]);
    }

    [Theory]
    [InlineData("SKY", 30)]
    [InlineData("PPS", 10)]
    [InlineData("TOFF", 10)]
    [InlineData("OSC", 30)]
    public void Each_class_uses_its_default_stale_after(string cls, int seconds)
    {
        Apply("""{"class":"SKY","device":"/dev/ttyACM0","hdop":0.9,"nSat":1,"satellites":[{"PRN":3,"used":true}]}""");
        Apply("""{"class":"PPS","device":"/dev/ttyACM0","real_sec":1,"real_nsec":0,"clock_sec":1,"clock_nsec":5}""");
        Apply("""{"class":"TOFF","device":"/dev/ttyACM0","real_sec":1,"real_nsec":0,"clock_sec":1,"clock_nsec":5}""");
        Apply("""{"class":"OSC","device":"/dev/ttyACM0","running":true,"delta":5}""");

        bool Served(DeviceSnapshot d) => cls switch
        {
            "SKY" => d.Sky(SkyField.Hdop) is not null && d.SatellitesVisible is not null && d.SatellitesUsed is not null
                && !d.Satellites.IsEmpty && d.SkyFields.Any(),
            "PPS" => d.Pps is not null,
            "TOFF" => d.Toff is not null,
            _ => d.Osc is not null,
        };

        _time.Advance(TimeSpan.FromSeconds(seconds));
        Served(Device(_store.Snapshot())).ShouldBeTrue();

        _time.Advance(TimeSpan.FromMilliseconds(1));
        var device = Device(_store.Snapshot());
        Served(device).ShouldBeFalse();
        if (cls == "SKY")
        {
            device.Satellites.ShouldBeEmpty();
            device.SkyFields.ShouldBeEmpty();
            device.SatellitesVisible.ShouldBeNull();
            device.SatellitesUsed.ShouldBeNull();
        }
    }

    [Fact]
    public void The_pulse_counter_does_not_go_stale()
    {
        Apply("""{"class":"PPS","device":"/dev/ttyACM0","real_sec":1,"real_nsec":0,"clock_sec":1,"clock_nsec":5}""");
        _time.Advance(TimeSpan.FromMinutes(5));

        var device = Device(_store.Snapshot());
        device.Pps.ShouldBeNull();
        device.PpsPulses.ShouldBe(1);
    }

    [Fact]
    public void No_pulse_counter_before_the_first_pps()
    {
        Apply(FullTpv);

        Device(_store.Snapshot()).PpsPulses.ShouldBeNull();
    }

    [Fact]
    public void Stale_after_settings_are_used()
    {
        var store = new StateStore(_time, Options.Create(new StalenessOptions { Tpv = TimeSpan.FromSeconds(2) }));
        Apply(store, FullTpv);

        _time.Advance(TimeSpan.FromSeconds(2));
        Device(store.Snapshot()).TpvMode.ShouldBe(3);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Device(store.Snapshot()).TpvMode.ShouldBeNull();
    }

    [Fact]
    public void Stale_after_defaults_match_the_spec()
    {
        var options = new StalenessOptions();

        new[] { GpsdClass.Tpv, GpsdClass.Sky, GpsdClass.Pps, GpsdClass.Toff, GpsdClass.Osc }
            .Select(c => options.For(c)!.Value.TotalSeconds)
            .ShouldBe([10, 30, 10, 10, 30]);
        options.For(GpsdClass.Version).ShouldBeNull();
        options.For(GpsdClass.Device).ShouldBeNull();
    }

    [Fact]
    public void a_snapshot_keeps_one_state_and_one_time()
    {
        Apply(FullTpv);
        var snapshot = _store.Snapshot();

        Apply("""{"class":"TPV","device":"/dev/ttyACM0","mode":1,"time":"2026-11-23T10:00:01.000Z"}""");
        Apply("""{"class":"DEVICE","path":"/dev/ttyACM0","activated":0}""");
        _time.Advance(TimeSpan.FromMinutes(1));

        snapshot.Now.ShouldBe(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
        var device = Device(snapshot);
        device.TpvMode.ShouldBe(3);
        device.Tpv(TpvField.Lat).ShouldBe(51.5);
        _store.Snapshot().Devices.ShouldBeEmpty();
    }

    [Fact]
    public void Devices_are_ordered_by_path()
    {
        Apply("""{"class":"TPV","device":"/dev/ttyUSB0","mode":1}""");
        Apply("""{"class":"TPV","device":"/dev/ttyACM0","mode":1}""");

        _store.Snapshot().Devices.Select(d => d.Path).ShouldBe(["/dev/ttyACM0", "/dev/ttyUSB0"]);
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] TornFields = ["speed", "eph", "ept"];

    [Fact]
    public async Task Snapshots_read_while_reports_are_applied_are_never_torn()
    {
        // The writer sends TPVs whose speed, eph and ept all equal the epoch number, one TPV per
        // field, all in the same epoch. A reader must never see a mix of two epochs' values in
        // the current epoch, nor a value from an epoch that has not finished being written.
        const int epochs = 5_000;
        var ct = TestContext.Current.CancellationToken;
        using var done = new CancellationTokenSource();
        var reports = Enumerable.Range(1, epochs)
            .SelectMany(i =>
            {
                var time = Start.AddSeconds(i).ToString("O", CultureInfo.InvariantCulture);
                return TornFields.Select(f =>
                    GpsdParser.Parse(Encoding.UTF8.GetBytes(
                        $$"""{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"{{time}}","{{f}}":{{i}}}""")).Report!);
            })
            .ToArray();

        var writer = Task.Run(
            () =>
            {
                foreach (var report in reports)
                {
                    _store.Apply(report);
                }

                done.Cancel();
            },
            ct);

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(
            () =>
            {
                var checks = 0;
                while (!done.IsCancellationRequested || checks == 0)
                {
                    var snapshot = _store.Snapshot();
                    if (snapshot.Devices.IsEmpty)
                    {
                        continue;
                    }

                    var tpv = snapshot.Devices[0].State.Tpv!;
                    var epoch = (tpv.Current.Time!.Value - Start).TotalSeconds;
                    foreach (var (_, value) in tpv.Current.Fields.Present)
                    {
                        value.Value.ShouldBe(epoch);
                    }

                    // Fields not yet in the current epoch come from the one before it.
                    foreach (var (_, value) in tpv.Served)
                    {
                        value.Value.ShouldBeInRange(epoch - 1, epoch);
                    }

                    checks++;
                }

                return checks;
            },
            ct)).ToArray();

        await writer;
        var counts = await Task.WhenAll(readers);

        counts.ShouldAllBe(c => c > 0);
        var final = _store.Snapshot().Devices.ShouldHaveSingleItem();
        final.Tpv(TpvField.Speed).ShouldBe(epochs);
        final.Tpv(TpvField.Eph).ShouldBe(epochs);
        final.Tpv(TpvField.Ept).ShouldBe(epochs);
    }
}
