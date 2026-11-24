using System.Text;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using GpsdExporter.Testing;
using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Core.State;

public sealed class SkyStateTests
{
    private const string Acm0 = "/dev/ttyACM0";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    private readonly StateStore _store;

    public SkyStateTests() => _store = new StateStore(_time);

    private void Apply(string line)
    {
        var parsed = GpsdParser.Parse(Encoding.UTF8.GetBytes(line));
        parsed.Error.ShouldBeNull(line);
        _store.Apply(parsed.Report!).ShouldBeNull(line);
    }

    private void Replay(string fixture)
    {
        foreach (var line in Fixtures.LoadBytes(fixture))
        {
            if (GpsdParser.Parse(line).Report is { } report)
            {
                _store.Apply(report).ShouldBeNull();
            }
        }
    }

    private SkyState Sky => _store.Current.Devices[Acm0].Sky.ShouldNotBeNull();

    private static string SkyLine(string? time, string fields) =>
        $$"""{"class":"SKY","device":"{{Acm0}}"{{(time is null ? "" : $",\"time\":\"{time}\"")}},{{fields}}}""";

    private static string Sat(int prn, bool used = true, double ss = 40) =>
        $$"""{"PRN":{{prn}},"gnssid":0,"svid":{{prn}},"sigid":0,"ss":{{ss}},"used":{{(used ? "true" : "false")}}}""";

    [Fact]
    public void No_sky_state_before_the_first_sky()
    {
        Apply("""{"class":"TPV","device":"/dev/ttyACM0","mode":3}""");

        _store.Current.Devices[Acm0].Sky.ShouldBeNull();
    }

    [Fact]
    public void satellites_missing_from_the_latest_array_are_gone()
    {
        Apply(SkyLine("2026-11-23T10:00:00.000Z", $"\"satellites\":[{Sat(3)},{Sat(8)},{Sat(10)}]"));
        Sky.Satellites!.Value.Value.Length.ShouldBe(3);

        Apply(SkyLine("2026-11-23T10:00:01.000Z", $"\"satellites\":[{Sat(8)}]"));

        Sky.Satellites!.Value.Value.ShouldHaveSingleItem().SvId.ShouldBe(8);
    }

    [Fact]
    public void replaying_the_sky_shrink_fixture_leaves_one_satellite()
    {
        Replay("session-sky-shrink.jsonl");

        var sky = Sky;
        sky.Satellites!.Value.Value.ShouldHaveSingleItem().Prn.ShouldBe(8);
        sky.Visible!.Value.Value.ShouldBe(1);
        sky.Used!.Value.Value.ShouldBe(1);
        sky[SkyField.Hdop]!.Value.Value.ShouldBe(2.1);
    }

    [Fact]
    public void An_empty_array_clears_the_satellites()
    {
        Apply(SkyLine("2026-11-23T10:00:00.000Z", $"\"satellites\":[{Sat(3)}]"));
        Apply(SkyLine("2026-11-23T10:00:01.000Z", "\"satellites\":[]"));

        Sky.Satellites!.Value.Value.ShouldBeEmpty();
        Sky.Visible!.Value.Value.ShouldBe(0);
    }

    [Fact]
    public void a_dop_only_sky_keeps_the_satellites_and_updates_the_dops()
    {
        var t0 = _time.GetUtcNow();
        Apply(SkyLine("2026-11-23T10:00:00.000Z", $"\"hdop\":0.8,\"pdop\":1.5,\"satellites\":[{Sat(3)},{Sat(8)}]"));
        _time.Advance(TimeSpan.FromSeconds(1));

        Apply(SkyLine("2026-11-23T10:00:01.000Z", "\"hdop\":0.9"));

        var sky = Sky;
        sky.Satellites!.Value.Value.Length.ShouldBe(2);
        sky.Satellites!.Value.ReceivedAt.ShouldBe(t0);
        sky[SkyField.Hdop]!.Value.Value.ShouldBe(0.9);
        sky[SkyField.Pdop]!.Value.Value.ShouldBe(1.5);
    }

    [Fact]
    public void replaying_the_partial_epochs_fixture_keeps_satellites_through_a_dop_only_sky()
    {
        Replay("session-partial-epochs.jsonl");

        Sky.Satellites.ShouldNotBeNull().Value.Length.ShouldBe(3);
        Sky[SkyField.Hdop]!.Value.Value.ShouldBe(0.85);
    }

    [Fact]
    public void Scalar_fields_follow_the_two_epoch_rule()
    {
        Apply(SkyLine("2026-11-23T10:00:00.000Z", "\"hdop\":0.8,\"gdop\":1.8"));
        Apply(SkyLine("2026-11-23T10:00:00.000Z", "\"vdop\":1.2"));
        Sky[SkyField.Gdop].ShouldNotBeNull();
        Sky[SkyField.Vdop].ShouldNotBeNull();

        Apply(SkyLine("2026-11-23T10:00:01.000Z", "\"hdop\":0.9"));
        Sky[SkyField.Gdop]!.Value.Value.ShouldBe(1.8);

        Apply(SkyLine("2026-11-23T10:00:02.000Z", "\"hdop\":1.0"));
        Sky[SkyField.Gdop].ShouldBeNull();
        Sky[SkyField.Vdop].ShouldBeNull();
    }

    [Fact]
    public void An_untimed_or_earlier_sky_starts_a_new_epoch()
    {
        Apply(SkyLine("2026-11-23T10:00:05.000Z", "\"gdop\":1.8"));
        Apply(SkyLine(null, "\"hdop\":0.9"));
        Sky[SkyField.Gdop].ShouldNotBeNull();

        Apply(SkyLine("2026-11-23T10:00:00.000Z", "\"hdop\":1.0"));
        Sky[SkyField.Gdop].ShouldBeNull();
        Sky.Epochs.Current.Time.ShouldBe(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Visible_and_used_come_from_nsat_and_usat_when_sent()
    {
        Apply(SkyLine("2026-11-23T10:00:00.000Z", $"\"nSat\":12,\"uSat\":9,\"satellites\":[{Sat(3)},{Sat(8, used: false)}]"));

        Sky.Visible!.Value.Value.ShouldBe(12);
        Sky.Used!.Value.Value.ShouldBe(9);
    }

    [Fact]
    public void Visible_and_used_are_derived_from_the_array_otherwise()
    {
        Apply(SkyLine("2026-11-23T10:00:00.000Z", $"\"satellites\":[{Sat(3)},{Sat(8, used: false)},{Sat(10)}]"));

        Sky.Visible!.Value.Value.ShouldBe(3);
        Sky.Used!.Value.Value.ShouldBe(2);
    }

    [Fact]
    public void Visible_and_used_are_absent_without_nsat_usat_or_an_array()
    {
        Apply(SkyLine("2026-11-23T10:00:00.000Z", "\"hdop\":0.8"));

        Sky.Visible.ShouldBeNull();
        Sky.Used.ShouldBeNull();
    }

    [Fact]
    public void a_zero_snr_is_kept()
    {
        Apply(SkyLine("2026-11-23T10:00:00.000Z", $"\"satellites\":[{Sat(29, used: false, ss: 0)}]"));

        Sky.Satellites!.Value.Value[0].Fields[SatelliteField.Ss].ShouldBe(0);
    }

    [Fact]
    public void the_multi_gnss_fixture_gives_one_entry_per_signal()
    {
        Replay("session-multi-gnss.jsonl");

        var satellites = Sky.Satellites!.Value.Value;
        satellites.Length.ShouldBe(15);
        satellites.Select(SatelliteId.Of).Distinct().Count().ShouldBe(15);

        // GPS svid 3 on two signals.
        satellites.Where(s => s is { GnssId: 0, SvId: 3 }).Select(s => s.SigId).ShouldBe([0, 6], ignoreOrder: true);

        // PRN-only satellites keep only their PRN.
        satellites.Where(s => s.GnssId is null).Select(s => s.Prn).ShouldBe([17, 29], ignoreOrder: true);

        // The later duplicate of GPS svid 8 sigid 0 won.
        satellites.Single(s => s is { GnssId: 0, SvId: 8, SigId: 0 }).Fields[SatelliteField.Ss].ShouldBe(45);

        // nSat counts every array entry, including the duplicate.
        Sky.Visible!.Value.Value.ShouldBe(16);
        satellites.Select(s => Constellation.FromGnssId(s.GnssId ?? -1)).Where(c => c != "unknown").Distinct()
            .ShouldBe(["gps", "sbas", "galileo", "beidou", "imes", "qzss", "glonass", "navic"], ignoreOrder: true);
    }

    [Fact]
    public void Devices_have_separate_sky_state()
    {
        Replay("session-two-devices.jsonl");

        _store.Current.Devices[Acm0].Sky![SkyField.Hdop]!.Value.Value.ShouldBe(0.8);
        _store.Current.Devices.ContainsKey("/dev/ttyUSB0").ShouldBeFalse();
    }
}
