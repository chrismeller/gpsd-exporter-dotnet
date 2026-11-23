using System.Text;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using GpsdExporter.Testing;
using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Unit.State;

public sealed class TimingStateTests
{
    private const string Acm0 = "/dev/ttyACM0";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    private readonly StateStore _store;

    public TimingStateTests() => _store = new StateStore(_time);

    private DeviceState Device => _store.Current.Devices[Acm0];

    private void Apply(string line)
    {
        var parsed = GpsdParser.Parse(Encoding.UTF8.GetBytes(line));
        parsed.Error.ShouldBeNull(line);
        _store.Apply(parsed.Report!).ShouldBeNull(line);
    }

    /// <summary>Replays a fixture; returns the device after each line.</summary>
    private List<DeviceState?> Replay(string fixture)
    {
        var states = new List<DeviceState?>();
        foreach (var line in Fixtures.LoadBytes(fixture))
        {
            if (GpsdParser.Parse(line).Report is { } report)
            {
                _store.Apply(report).ShouldBeNull();
            }

            _time.Advance(TimeSpan.FromMilliseconds(100));
            states.Add(_store.Current.Devices.GetValueOrDefault(Acm0));
        }

        return states;
    }

    private static TimeOffsetState Offset(long realSec, long realNsec, long clockSec, long clockNsec) =>
        new(new TimeOffsetReport(Acm0, GpsdClass.Pps, realSec, realNsec, clockSec, clockNsec, null, null), DateTimeOffset.UnixEpoch);

    [Fact]
    public void T3_5_the_man_page_example_is_plus_2_ns_exactly()
    {
        var state = Offset(1_330_212_592, 343_182, 1_330_212_592, 343_184);

        state.OffsetNanoseconds.ShouldBe((Int128)2);
        state.OffsetSeconds.ShouldBe(2e-9);
    }

    [Theory]
    [InlineData(1_795_428_001, 0, 1_795_428_000, 998_500_000, -1_500_000)] // clock behind by 1.5 ms
    [InlineData(1_795_428_002, 999_999_999, 1_795_428_003, 0, 1)] // roll-over forwards
    [InlineData(1_795_428_003, 1, 1_795_428_002, 999_999_999, -2)] // roll-over backwards
    [InlineData(1_795_428_004, 0, 1_795_428_004, 250, 250)]
    [InlineData(100, 0, 0, 0, -100_000_000_000)]
    [InlineData(0, 999_999_999, long.MaxValue / 2, 0, (long.MaxValue / 2 * 1_000_000_000.0) - 999_999_999)]
    public void T3_5_negative_differences_and_second_roll_over(long realSec, long realNsec, long clockSec, long clockNsec, double expectedNs)
    {
        var state = Offset(realSec, realNsec, clockSec, clockNsec);

        ((double)state.OffsetNanoseconds).ShouldBe(expectedNs);
        state.OffsetSeconds.ShouldBe(expectedNs / 1e9, tolerance: Math.Abs(expectedNs) * 1e-15);
    }

    [Fact]
    public void T3_5_offset_is_exact_in_nanoseconds_far_from_the_epoch()
    {
        // In floating point seconds, 1795428003.000000000 - 1795428002.999999999 is not 1 ns.
        Offset(1_795_428_002, 999_999_999, 1_795_428_003, 0).OffsetNanoseconds.ShouldBe((Int128)1);
    }

    [Fact]
    public void Quantization_error_and_pulse_time_are_converted_to_seconds()
    {
        var report = new TimeOffsetReport(Acm0, GpsdClass.Pps, 1, 0, 1_795_428_000, 500_000_000, -20, -12_345);
        var state = new TimeOffsetState(report, DateTimeOffset.UnixEpoch);

        state.QuantizationErrorSeconds!.Value.ShouldBe(-12_345e-12, tolerance: 1e-24);
        state.PrecisionLog2Seconds.ShouldBe(-20);
        state.PulseTimestampSeconds.ShouldBe(1_795_428_000.5);
    }

    [Fact]
    public void Osc_delta_is_converted_to_seconds()
    {
        var state = new OscState(new OscReport(Acm0, true, false, null, -1250), DateTimeOffset.UnixEpoch);

        state.DeltaSeconds!.Value.ShouldBe(-1.25e-6, tolerance: 1e-18);
        (state.Running, state.Reference, state.Disciplined).ShouldBe((true, false, (bool?)null));
    }

    [Fact]
    public void No_timing_state_before_the_first_report()
    {
        Apply("""{"class":"TPV","device":"/dev/ttyACM0","mode":3}""");

        (Device.Pps, Device.Toff, Device.Osc, Device.PpsPulses).ShouldBe((null, null, null, 0L));
    }

    [Fact]
    public void T3_4_and_T3_5_replaying_the_timing_fixture()
    {
        var states = Replay("session-timing.jsonl");

        // Lines: 0-2 preamble, 3 TOFF, 4 PPS (qErr), 5 PPS (no qErr), 6 PPS, 7 TOFF, 8 TOFF (no precision), 9-10 OSC.
        states[3]!.Toff!.OffsetNanoseconds.ShouldBe((Int128)2);
        states[3]!.Pps.ShouldBeNull();

        states[4]!.Pps!.OffsetNanoseconds.ShouldBe((Int128)2);
        states[4]!.Pps!.QuantizationErrorSeconds.ShouldNotBeNull();
        states[4]!.Pps!.PrecisionLog2Seconds.ShouldBe(-3);

        // T3.4: a PPS without qErr replaces the one with it, so no quantization error.
        states[5]!.Pps!.OffsetNanoseconds.ShouldBe((Int128)(-1_500_000));
        states[5]!.Pps!.QuantizationErrorSeconds.ShouldBeNull();

        states[6]!.Pps!.OffsetNanoseconds.ShouldBe((Int128)1);
        states[7]!.Toff!.OffsetNanoseconds.ShouldBe((Int128)(-2));
        states[8]!.Toff!.PrecisionLog2Seconds.ShouldBeNull();
        states[8]!.Toff!.OffsetNanoseconds.ShouldBe((Int128)250);

        states[9]!.Osc!.DeltaSeconds!.Value.ShouldBe(67e-9, tolerance: 1e-18);
        states[10]!.Osc!.Reference.ShouldBe(false);
        states[10]!.Osc!.DeltaSeconds!.Value.ShouldBe(-1.25e-6, tolerance: 1e-18);

        // Timing never disturbs the other classes' state, and vice versa.
        states[10]!.Pps!.OffsetNanoseconds.ShouldBe((Int128)1);
        states[10]!.Toff!.OffsetNanoseconds.ShouldBe((Int128)250);
        states[10]!.PpsPulses.ShouldBe(3);
    }

    [Fact]
    public void Each_report_records_when_it_was_received()
    {
        var t0 = _time.GetUtcNow();
        Apply("""{"class":"PPS","device":"/dev/ttyACM0","real_sec":1,"real_nsec":0,"clock_sec":1,"clock_nsec":0}""");
        _time.Advance(TimeSpan.FromSeconds(1));
        Apply("""{"class":"OSC","device":"/dev/ttyACM0","running":true}""");

        Device.Pps!.ReceivedAt.ShouldBe(t0);
        Device.Osc!.ReceivedAt.ShouldBe(t0.AddSeconds(1));
        Device.LastReport[GpsdClass.Pps].ShouldBe(t0);
        Device.LastReport[GpsdClass.Osc].ShouldBe(t0.AddSeconds(1));
    }

    [Fact]
    public void Pulse_count_starts_again_after_device_removal_and_reset()
    {
        const string pps = """{"class":"PPS","device":"/dev/ttyACM0","real_sec":1,"real_nsec":0,"clock_sec":1,"clock_nsec":0}""";
        Apply(pps);
        Apply(pps);
        Device.PpsPulses.ShouldBe(2);

        Apply("""{"class":"DEVICE","path":"/dev/ttyACM0","activated":0}""");
        Apply(pps);
        Device.PpsPulses.ShouldBe(1);

        _store.Reset();
        Apply(pps);
        Device.PpsPulses.ShouldBe(1);
    }

    [Fact]
    public void Toff_does_not_count_as_a_pulse()
    {
        Apply("""{"class":"TOFF","device":"/dev/ttyACM0","real_sec":1,"real_nsec":0,"clock_sec":1,"clock_nsec":0}""");

        Device.PpsPulses.ShouldBe(0);
        Device.Pps.ShouldBeNull();
    }
}
