using System.Text;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using GpsdExporter.Testing;
using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Unit.State;

public sealed class StateStoreTests
{
    private const string Acm0 = "/dev/ttyACM0";
    private const string Usb0 = "/dev/ttyUSB0";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    private readonly StateStore _store;

    public StateStoreTests() => _store = new StateStore(_time);

    private ParseErrorReason? Apply(string line)
    {
        var parsed = GpsdParser.Parse(Encoding.UTF8.GetBytes(line));
        parsed.Error.ShouldBeNull(line);
        return _store.Apply(parsed.Report!);
    }

    private void ApplyAll(params string[] lines)
    {
        foreach (var line in lines)
        {
            Apply(line).ShouldBeNull(line);
        }
    }

    /// <summary>Replays a fixture; returns the state after each line.</summary>
    private List<GpsdState> Replay(string fixture)
    {
        var states = new List<GpsdState>();
        foreach (var line in Fixtures.LoadBytes(fixture))
        {
            var parsed = GpsdParser.Parse(line);
            if (parsed.Report is not null)
            {
                _store.Apply(parsed.Report).ShouldBeNull(Encoding.UTF8.GetString(line));
            }

            _time.Advance(TimeSpan.FromMilliseconds(100));
            states.Add(_store.Current);
        }

        return states;
    }

    private TpvState Tpv(string device = Acm0) => _store.Current.Devices[device].Tpv.ShouldNotBeNull();

    private static string TpvLine(int mode, string? time, string fields = "", string device = Acm0)
    {
        var timePart = time is null ? string.Empty : $",\"time\":\"{time}\"";
        var fieldPart = fields.Length == 0 ? string.Empty : "," + fields;
        return $$"""{"class":"TPV","device":"{{device}}","mode":{{mode}}{{timePart}}{{fieldPart}}}""";
    }

    private static string ActiveDevice(string path) =>
        $$"""{"class":"DEVICE","path":"{{path}}","driver":"u-blox","activated":"2026-11-23T09:59:58.000Z"}""";

    [Fact]
    public void Starts_empty()
    {
        _store.Current.Version.ShouldBeNull();
        _store.Current.Devices.ShouldBeEmpty();
    }

    [Fact]
    public void T4_1_two_tpvs_with_the_same_time_are_merged()
    {
        ApplyAll(
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5,\"lon\":-0.1"),
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"speed\":0.04"));

        var tpv = Tpv();
        tpv[TpvField.Lat]!.Value.Value.ShouldBe(51.5);
        tpv[TpvField.Lon]!.Value.Value.ShouldBe(-0.1);
        tpv[TpvField.Speed]!.Value.Value.ShouldBe(0.04);
    }

    [Fact]
    public void T4_1_a_field_sent_again_in_the_same_epoch_takes_the_new_value()
    {
        ApplyAll(
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"speed\":1"),
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"speed\":2"));

        Tpv()[TpvField.Speed]!.Value.Value.ShouldBe(2);
    }

    [Fact]
    public void T4_2_a_field_is_served_for_one_more_epoch_then_dropped()
    {
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"epx\":1.5,\"lat\":51.5"));
        Tpv()[TpvField.Epx].ShouldNotBeNull();

        Apply(TpvLine(3, "2026-11-23T10:00:01.000Z", "\"lat\":51.5"));
        Tpv()[TpvField.Epx]!.Value.Value.ShouldBe(1.5);

        Apply(TpvLine(3, "2026-11-23T10:00:02.000Z", "\"lat\":51.5"));
        Tpv()[TpvField.Epx].ShouldBeNull();
        Tpv()[TpvField.Lat].ShouldNotBeNull();
    }

    [Fact]
    public void T4_2_replaying_the_partial_epochs_fixture_drops_epx_after_one_epoch()
    {
        var states = Replay("session-partial-epochs.jsonl");

        double? Epx(GpsdState s) => s.Devices[Acm0].Tpv?[TpvField.Epx]?.Value;
        double? Climb(GpsdState s) => s.Devices[Acm0].Tpv?[TpvField.Climb]?.Value;

        // Lines: 0 VERSION, 1 DEVICES, 2 WATCH, 3-4 epoch 1 (epx), 5 SKY, 6-7 epoch 2, 8 SKY, 9-10 epoch 3.
        Epx(states[3]).ShouldBe(1.5);
        Epx(states[6]).ShouldBe(1.5);
        Epx(states[7]).ShouldBe(1.5);
        Epx(states[9]).ShouldBeNull();

        // The second TPV of an epoch carries climb; it never flickers out between the two.
        Climb(states[4]).ShouldBe(0.011);
        Climb(states[6]).ShouldBe(0.011);
        Climb(states[7]).ShouldBe(0.01);
        Climb(states[9]).ShouldBe(0.01);
    }

    [Fact]
    public void T4_3_mode_1_removes_position_velocity_error_and_timestamp()
    {
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z",
            "\"lat\":51.5,\"lon\":-0.1,\"altHAE\":92.1,\"speed\":0.04,\"climb\":0.01,\"track\":87.5,\"velN\":0.1," +
            "\"ecefx\":3978000.1,\"eph\":2.4,\"epv\":3.2,\"epx\":1.5,\"eps\":0.3,\"ept\":0.005,\"leapseconds\":18,\"temp\":31.5"));
        Tpv().Timestamp.ShouldNotBeNull();

        Apply(TpvLine(1, "2026-11-23T10:00:01.000Z", "\"ept\":0.005"));

        var tpv = Tpv();
        tpv.Mode.Value.ShouldBe(1);
        tpv.Timestamp.ShouldBeNull();
        foreach (var field in TpvState.FixFields.Concat(TpvState.ThreeDFields))
        {
            tpv[field].ShouldBeNull(field.ToString());
        }

        // "Field present" rows are not tied to the fix and follow the epoch rule.
        tpv[TpvField.Ept]!.Value.Value.ShouldBe(0.005);
        tpv[TpvField.LeapSeconds]!.Value.Value.ShouldBe(18);
        tpv[TpvField.Temp]!.Value.Value.ShouldBe(31.5);
    }

    [Fact]
    public void T4_3_mode_0_without_time_also_removes_the_fix()
    {
        ApplyAll(
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5,\"altHAE\":92.1"),
            TpvLine(0, null));

        var tpv = Tpv();
        tpv.Mode.Value.ShouldBe(0);
        tpv[TpvField.Lat].ShouldBeNull();
        tpv[TpvField.AltHae].ShouldBeNull();
        tpv.Served.ShouldBeEmpty();
    }

    [Fact]
    public void T4_3_a_fix_from_before_the_loss_does_not_come_back()
    {
        ApplyAll(
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5,\"epx\":1.5"),
            TpvLine(1, "2026-11-23T10:00:01.000Z"),
            TpvLine(3, "2026-11-23T10:00:02.000Z", "\"lon\":-0.1"));

        var tpv = Tpv();
        tpv[TpvField.Lat].ShouldBeNull();
        tpv[TpvField.Epx].ShouldBeNull();
        tpv[TpvField.Lon].ShouldNotBeNull();
    }

    [Fact]
    public void T4_4_mode_2_removes_altitude_climb_and_vertical_error_and_keeps_horizontal()
    {
        ApplyAll(
            TpvLine(3, "2026-11-23T10:00:00.000Z",
                "\"lat\":51.5,\"lon\":-0.1,\"altHAE\":92.1,\"altMSL\":45.6,\"climb\":0.01,\"epv\":3.2,\"epc\":0.5,\"eph\":2.4,\"speed\":0.04"),
            TpvLine(2, "2026-11-23T10:00:01.000Z", "\"lat\":51.6"));

        var tpv = Tpv();
        tpv.Mode.Value.ShouldBe(2);
        foreach (var field in TpvState.ThreeDFields)
        {
            tpv[field].ShouldBeNull(field.ToString());
        }

        tpv[TpvField.Lat]!.Value.Value.ShouldBe(51.6);
        tpv[TpvField.Lon]!.Value.Value.ShouldBe(-0.1);
        tpv[TpvField.Eph]!.Value.Value.ShouldBe(2.4);
        tpv[TpvField.Speed]!.Value.Value.ShouldBe(0.04);
        tpv.Timestamp!.Value.Value.ShouldBe(new DateTimeOffset(2026, 11, 23, 10, 0, 1, TimeSpan.Zero));
    }

    [Fact]
    public void A_3d_field_sent_with_mode_2_is_not_served()
    {
        Apply(TpvLine(2, "2026-11-23T10:00:00.000Z", "\"lat\":51.5,\"altHAE\":92.1"));

        Tpv()[TpvField.AltHae].ShouldBeNull();
        Tpv()[TpvField.Lat].ShouldNotBeNull();
    }

    [Fact]
    public void The_fix_loss_fixture_drops_and_restores_fields_at_the_right_lines()
    {
        var states = Replay("session-fix-loss.jsonl");

        // Lines 3 to 10 carry modes 3, 3, 2, 1, 0, 1, 2, 3.
        int Mode(int i) => states[i].Devices[Acm0].Tpv!.Mode.Value;
        bool Has(int i, TpvField f) => states[i].Devices[Acm0].Tpv![f] is not null;

        Enumerable.Range(3, 8).Select(Mode).ShouldBe([3, 3, 2, 1, 0, 1, 2, 3]);
        Has(4, TpvField.Lat).ShouldBeTrue();
        Has(5, TpvField.Lat).ShouldBeTrue();
        Has(6, TpvField.Lat).ShouldBeFalse();
        Has(7, TpvField.Lat).ShouldBeFalse();
        Has(8, TpvField.Lat).ShouldBeFalse();
        Has(9, TpvField.Lat).ShouldBeTrue();
        Has(10, TpvField.Lat).ShouldBeTrue();
        Has(5, TpvField.Epv).ShouldBeFalse();
        Has(9, TpvField.Epv).ShouldBeFalse();
    }

    [Fact]
    public void T4_13_each_tpv_without_time_is_its_own_epoch()
    {
        ApplyAll(
            TpvLine(1, null, "\"ept\":0.5"),
            TpvLine(1, null, "\"temp\":30"));
        var tpv = Tpv();
        tpv[TpvField.Ept].ShouldNotBeNull();
        tpv[TpvField.Temp].ShouldNotBeNull();
        tpv.Current.Time.ShouldBeNull();

        Apply(TpvLine(1, null, "\"leapseconds\":18"));

        // ept was two epochs ago.
        Tpv()[TpvField.Ept].ShouldBeNull();
        Tpv()[TpvField.Temp].ShouldNotBeNull();
    }

    [Fact]
    public void T4_13_an_earlier_time_starts_a_new_epoch()
    {
        ApplyAll(
            TpvLine(3, "2026-11-23T10:00:05.000Z", "\"lat\":51.5,\"epx\":1.5"),
            TpvLine(3, "2026-11-23T10:00:06.000Z", "\"lat\":51.6"),
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.7"));

        var tpv = Tpv();
        tpv.Current.Time.ShouldBe(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
        tpv.Previous!.Time.ShouldBe(new DateTimeOffset(2026, 11, 23, 10, 0, 6, TimeSpan.Zero));
        tpv[TpvField.Lat]!.Value.Value.ShouldBe(51.7);
        tpv[TpvField.Epx].ShouldBeNull();
        tpv.Timestamp!.Value.Value.ShouldBe(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void T4_13_a_time_matching_an_older_epoch_is_not_merged_into_it()
    {
        ApplyAll(
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5"),
            TpvLine(3, "2026-11-23T10:00:01.000Z", "\"lat\":51.6"),
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lon\":-0.1"));

        var tpv = Tpv();
        tpv.Current.Fields[TpvField.Lat].ShouldBeNull();
        tpv[TpvField.Lat]!.Value.Value.ShouldBe(51.6);
    }

    [Fact]
    public void Every_value_carries_the_time_it_was_received()
    {
        var t0 = _time.GetUtcNow();
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5"));
        _time.Advance(TimeSpan.FromMilliseconds(300));
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"speed\":0.1"));

        var tpv = Tpv();
        tpv[TpvField.Lat]!.Value.ReceivedAt.ShouldBe(t0);
        tpv[TpvField.Speed]!.Value.ReceivedAt.ShouldBe(t0.AddMilliseconds(300));
        tpv.Mode.ReceivedAt.ShouldBe(t0.AddMilliseconds(300));
        tpv.Timestamp!.Value.ReceivedAt.ShouldBe(t0.AddMilliseconds(300));
        _store.Current.Devices[Acm0].LastReport[GpsdClass.Tpv].ShouldBe(t0.AddMilliseconds(300));
    }

    [Fact]
    public void Jam_minus_one_is_not_served_and_zero_is()
    {
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"jam\":-1,\"ant\":0"));

        Tpv()[TpvField.Jam].ShouldBeNull();
        Tpv()[TpvField.Ant]!.Value.Value.ShouldBe(0);

        Apply(TpvLine(3, "2026-11-23T10:00:01.000Z", "\"jam\":0"));
        Tpv()[TpvField.Jam]!.Value.Value.ShouldBe(0);
    }

    [Fact]
    public void Version_is_kept()
    {
        Apply("""{"class":"VERSION","release":"3.22","rev":"3.22","proto_major":3,"proto_minor":14}""");

        _store.Current.Version!.Value.Value.Release.ShouldBe("3.22");
    }

    [Fact]
    public void Devices_creates_every_listed_device_with_its_info()
    {
        Apply("""{"class":"DEVICES","devices":[{"class":"DEVICE","path":"/dev/ttyACM0","driver":"u-blox","subtype":"HW 00190000","activated":"2026-11-23T09:59:58.000Z","cycle":1.0,"bps":9600},{"class":"DEVICE","path":"/dev/ttyUSB0","driver":"NMEA0183"}]}""")
            .ShouldBeNull();

        _store.Current.Devices.Keys.ShouldBe([Acm0, Usb0]);
        var info = _store.Current.Devices[Acm0].Info.ShouldNotBeNull();
        (info.Driver, info.Subtype, info.Cycle, info.Bps).ShouldBe(("u-blox", "HW 00190000", 1.0, 9600.0));
        _store.Current.Devices[Acm0].Tpv.ShouldBeNull();
    }

    [Fact]
    public void A_device_created_by_another_report_has_no_info()
    {
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", device: Usb0));

        _store.Current.Devices[Usb0].Info.ShouldBeNull();
        _store.Current.Devices[Usb0].Tpv.ShouldNotBeNull();
    }

    [Fact]
    public void A_device_listed_later_keeps_its_tpv()
    {
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5"));
        Apply(ActiveDevice(Acm0));

        _store.Current.Devices[Acm0].Info.ShouldNotBeNull();
        Tpv()[TpvField.Lat].ShouldNotBeNull();
    }

    [Fact]
    public void T4_8_deactivating_one_of_two_devices_removes_only_that_device()
    {
        ApplyAll(
            ActiveDevice(Acm0),
            ActiveDevice(Usb0),
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5"),
            TpvLine(2, "2026-11-23T10:00:00.000Z", "\"lat\":40.6", Usb0));

        Apply($$"""{"class":"DEVICE","path":"{{Usb0}}","activated":0}""").ShouldBeNull();

        _store.Current.Devices.Keys.ShouldBe([Acm0]);
        Tpv()[TpvField.Lat]!.Value.Value.ShouldBe(51.5);
    }

    [Fact]
    public void T4_8_replaying_the_two_devices_fixture_keeps_devices_apart_until_one_goes()
    {
        var states = Replay("session-two-devices.jsonl");

        var before = states[^3];
        before.Devices.Keys.ShouldBe([Acm0, Usb0]);
        before.Devices[Acm0].Tpv![TpvField.Lat]!.Value.Value.ShouldBe(51.477811);
        before.Devices[Usb0].Tpv![TpvField.Lat]!.Value.Value.ShouldBe(40.689247);
        before.Devices[Acm0].Tpv![TpvField.AltHae].ShouldNotBeNull();
        before.Devices[Usb0].Tpv![TpvField.AltHae].ShouldBeNull();

        states[^2].Devices.Keys.ShouldBe([Acm0]);
        states[^1].Devices.Keys.ShouldBe([Acm0]);
    }

    [Fact]
    public void A_device_without_activated_is_removed()
    {
        ApplyAll(ActiveDevice(Acm0), ActiveDevice(Usb0));

        Apply($$"""{"class":"DEVICE","path":"{{Usb0}}"}""").ShouldBeNull();

        _store.Current.Devices.Keys.ShouldBe([Acm0]);
    }

    [Fact]
    public void Removing_an_unknown_device_is_harmless()
    {
        Apply($$"""{"class":"DEVICE","path":"{{Usb0}}","activated":0}""").ShouldBeNull();

        _store.Current.Devices.ShouldBeEmpty();
    }

    [Fact]
    public void T6_5_an_object_without_device_is_attributed_to_the_single_device()
    {
        Apply(ActiveDevice(Acm0));

        Apply("""{"class":"TPV","mode":3,"lat":51.5}""").ShouldBeNull();

        Tpv()[TpvField.Lat]!.Value.Value.ShouldBe(51.5);
    }

    [Fact]
    public void T6_5_an_object_without_device_is_skipped_with_two_devices()
    {
        ApplyAll(ActiveDevice(Acm0), ActiveDevice(Usb0));

        Apply("""{"class":"TPV","mode":3,"lat":51.5}""").ShouldBe(ParseErrorReason.MissingDevice);

        _store.Current.Devices.Values.ShouldAllBe(d => d.Tpv == null);
    }

    [Fact]
    public void T6_5_an_object_without_device_is_skipped_with_no_devices()
    {
        Apply("""{"class":"TPV","mode":3,"lat":51.5}""").ShouldBe(ParseErrorReason.MissingDevice);

        _store.Current.Devices.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"class":"SKY","hdop":1.2}""")]
    [InlineData("""{"class":"PPS","real_sec":1,"real_nsec":2,"clock_sec":3,"clock_nsec":4}""")]
    [InlineData("""{"class":"TOFF","real_sec":1,"real_nsec":2,"clock_sec":3,"clock_nsec":4}""")]
    [InlineData("""{"class":"OSC","running":true}""")]
    public void T6_5_applies_to_every_device_class(string line)
    {
        Apply(line).ShouldBe(ParseErrorReason.MissingDevice);
        Apply(ActiveDevice(Acm0));

        Apply(line).ShouldBeNull();
        _store.Current.Devices[Acm0].LastReport.Count.ShouldBe(2);
    }

    [Fact]
    public void T6_6_at_most_16_devices_are_tracked()
    {
        for (var i = 0; i < StateStore.MaxDevices; i++)
        {
            Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":1", $"/dev/ttyUSB{i}")).ShouldBeNull();
        }

        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":1", "/dev/ttyUSB16")).ShouldBe(ParseErrorReason.TooManyDevices);
        Apply(ActiveDevice("/dev/ttyUSB17")).ShouldBe(ParseErrorReason.TooManyDevices);
        Apply("""{"class":"DEVICES","devices":[{"class":"DEVICE","path":"/dev/ttyUSB18","activated":"2026-11-23T09:59:58.000Z"}]}""")
            .ShouldBe(ParseErrorReason.TooManyDevices);

        _store.Current.Devices.Count.ShouldBe(16);
        _store.Current.Devices.ContainsKey("/dev/ttyUSB16").ShouldBeFalse();

        // Tracked devices still update, and a removal frees a slot.
        Apply(TpvLine(3, "2026-11-23T10:00:01.000Z", "\"lat\":2", "/dev/ttyUSB0")).ShouldBeNull();
        Apply("""{"class":"DEVICE","path":"/dev/ttyUSB3","activated":0}""").ShouldBeNull();
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":1", "/dev/ttyUSB16")).ShouldBeNull();
        _store.Current.Devices.Count.ShouldBe(16);
    }

    [Fact]
    public void Watch_error_and_other_classes_change_nothing()
    {
        Apply(ActiveDevice(Acm0));
        var before = _store.Current;

        ApplyAll(
            """{"class":"WATCH","enable":true,"json":true}""",
            """{"class":"ERROR","message":"Unrecognized request '?FOO'"}""",
            """{"class":"AIS","device":"/dev/ttyUSB1","type":1}""");

        _store.Current.ShouldBeSameAs(before);
    }

    [Fact]
    public void AC4_5_reset_forgets_everything()
    {
        ApplyAll(
            """{"class":"VERSION","release":"3.22","rev":"3.22","proto_major":3,"proto_minor":14}""",
            ActiveDevice(Acm0),
            TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5"));

        _store.Reset();

        _store.Current.Version.ShouldBeNull();
        _store.Current.Devices.ShouldBeEmpty();
    }

    [Fact]
    public void An_earlier_state_is_not_changed_by_later_reports()
    {
        Apply(TpvLine(3, "2026-11-23T10:00:00.000Z", "\"lat\":51.5,\"altHAE\":92.1"));
        var before = _store.Current;

        ApplyAll(
            TpvLine(1, "2026-11-23T10:00:01.000Z"),
            $$"""{"class":"DEVICE","path":"{{Acm0}}","activated":0}""");

        before.Devices[Acm0].Tpv![TpvField.Lat]!.Value.Value.ShouldBe(51.5);
        before.Devices[Acm0].Tpv![TpvField.AltHae].ShouldNotBeNull();
        _store.Current.Devices.ShouldBeEmpty();
    }
}
