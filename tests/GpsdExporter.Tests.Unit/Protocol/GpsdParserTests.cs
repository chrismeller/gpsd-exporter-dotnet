using System.Text;
using System.Text.Json;
using GpsdExporter.Protocol;
using GpsdExporter.Testing;

namespace GpsdExporter.Tests.Unit.Protocol;

public sealed class GpsdParserTests
{
    private static ParseResult Parse(string line) => GpsdParser.Parse(Encoding.UTF8.GetBytes(line));

    private static T Accepted<T>(ParseResult result)
        where T : GpsdReport
    {
        result.Error.ShouldBeNull();
        return result.Report.ShouldBeOfType<T>();
    }

    // T2.1: lines longer than gpsd's own 10,240-character limit are the line reader's concern
    // (AC2.1); truncating them at every offset is quadratic and adds nothing here.
    private const int GpsdMaxLine = 10_240;

    public static TheoryData<string> AllFixtures() => [.. Fixtures.All()];

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void T2_1_every_prefix_of_every_fixture_line_is_parsed_or_rejected_as_invalid_json(string fixture)
    {
        foreach (var line in Fixtures.LoadBytes(fixture))
        {
            // The whole line never throws, whatever it holds.
            _ = GpsdParser.Parse(line);

            if (line.Length > GpsdMaxLine)
            {
                continue;
            }

            for (var length = 0; length < line.Length; length++)
            {
                var prefix = line.AsSpan(0, length);
                var result = GpsdParser.Parse(prefix);
                if (result.Error != ParseErrorReason.InvalidJson)
                {
                    IsValidJson(prefix).ShouldBeTrue(
                        $"{fixture}: prefix of {length} bytes was not invalid_json but is not JSON either: " +
                        Encoding.UTF8.GetString(prefix));
                }
            }
        }
    }

    private static bool IsValidJson(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(bytes);
            while (reader.Read())
            {
            }

            return reader.BytesConsumed > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"TPV\"")]
    [InlineData("null")]
    public void T2_2_a_value_that_is_not_an_object_is_not_object(string line)
    {
        var result = Parse(line);

        result.Error.ShouldBe(ParseErrorReason.NotObject);
        result.Report.ShouldBeNull();
        result.Class.ShouldBeNull();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"class\":5}")]
    [InlineData("{\"class\":null}")]
    [InlineData("{\"device\":\"/dev/ttyACM0\",\"mode\":3}")]
    public void T2_3_an_object_without_a_string_class_is_missing_class(string line)
    {
        var result = Parse(line);

        result.Error.ShouldBe(ParseErrorReason.MissingClass);
        result.Report.ShouldBeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"class\"=\"DEVICES\",\"devices\":[]}")]
    [InlineData("{\"class\":\"TPV\",\"mode\":3,")]
    [InlineData("{\"class\":\"TPV\",\"mode\":3} trailing")]
    [InlineData("")]
    public void Malformed_json_is_invalid_json(string line) =>
        Parse(line).Error.ShouldBe(ParseErrorReason.InvalidJson);

    [Fact]
    public void Invalid_utf8_is_invalid_json()
    {
        byte[] line = [.. "{\"class\":\"TPV\",\"mode\":3,\"time\":\""u8, 0xC3, 0x28, .. "\"}"u8];

        GpsdParser.Parse(line).Error.ShouldBe(ParseErrorReason.InvalidJson);
    }

    [Fact]
    public void Nesting_deeper_than_the_limit_is_invalid_json()
    {
        var line = "{\"class\":\"OTHER\",\"x\":" + new string('[', 40) + new string(']', 40) + "}";

        Parse(line).Error.ShouldBe(ParseErrorReason.InvalidJson);
    }

    [Fact]
    public void T2_4_a_lat_with_the_wrong_type_is_dropped_and_the_rest_used()
    {
        var result = Parse("""{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"2026-11-23T10:00:02.000Z","lat":"x","lon":-0.001475,"speed":0.04}""");

        var tpv = Accepted<TpvReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.BadField, "lat")]);
        tpv.Fields.Has(TpvField.Lat).ShouldBeFalse();
        tpv.Fields[TpvField.Lon].ShouldBe(-0.001475);
        tpv.Fields[TpvField.Speed].ShouldBe(0.04);
        tpv.Mode.ShouldBe(3);
        tpv.Time.ShouldBe(new DateTimeOffset(2026, 11, 23, 10, 0, 2, TimeSpan.Zero));
        tpv.Device.ShouldBe("/dev/ttyACM0");
    }

    [Fact]
    public void T2_4_a_number_that_overflows_to_infinity_is_dropped_and_the_rest_used()
    {
        var result = Parse("""{"class":"TPV","device":"/dev/ttyACM0","mode":3,"lat":51.477811,"lon":1e400,"speed":0.04}""");

        var tpv = Accepted<TpvReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.BadField, "lon")]);
        tpv.Fields.Has(TpvField.Lon).ShouldBeFalse();
        tpv.Fields[TpvField.Lat].ShouldBe(51.477811);
        tpv.Fields[TpvField.Speed].ShouldBe(0.04);
    }

    [Fact]
    public void T2_4_an_unparseable_time_is_dropped_and_the_rest_used()
    {
        var result = Parse("""{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"garbage","lat":51.477811,"lon":-0.001475}""");

        var tpv = Accepted<TpvReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.BadField, "time")]);
        tpv.Time.ShouldBeNull();
        tpv.Fields[TpvField.Lat].ShouldBe(51.477811);
        tpv.Fields[TpvField.Lon].ShouldBe(-0.001475);
    }

    [Theory]
    [InlineData("\"lat\":90.5", "lat")]
    [InlineData("\"lat\":-90.5", "lat")]
    [InlineData("\"lon\":180.5", "lon")]
    [InlineData("\"speed\":true", "speed")]
    [InlineData("\"device\":7", "device")]
    [InlineData("\"time\":[]", "time")]
    public void T2_4_out_of_range_and_wrong_typed_fields_are_bad_field(string field, string name)
    {
        var result = Parse($$"""{"class":"TPV","mode":2,{{field}},"eph":5.5}""");

        var tpv = Accepted<TpvReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.BadField, name)]);
        tpv.Fields[TpvField.Eph].ShouldBe(5.5);
    }

    [Fact]
    public void A_null_field_is_dropped_as_bad_field()
    {
        // gpsd never sends null; it leaves the field out. A null is the wrong type.
        var result = Parse("""{"class":"TPV","mode":2,"lat":null,"eph":5.5}""");

        var tpv = Accepted<TpvReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.BadField, "lat")]);
        tpv.Fields.Has(TpvField.Lat).ShouldBeFalse();
        tpv.Fields[TpvField.Eph].ShouldBe(5.5);
    }

    [Fact]
    public void Jam_minus_one_means_unset()
    {
        var tpv = Accepted<TpvReport>(Parse("""{"class":"TPV","mode":3,"jam":-1}"""));

        tpv.Fields.Has(TpvField.Jam).ShouldBeFalse();
    }

    [Fact]
    public void A_numeric_time_in_epoch_seconds_is_accepted()
    {
        var sky = Accepted<SkyReport>(Parse("""{"class":"SKY","time":1270517264.240,"hdop":9.2}"""));

        sky.Time.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1_270_517_264_240));
    }

    [Fact]
    public void T2_5_a_tpv_without_mode_is_skipped_as_missing_field()
    {
        var result = Parse("""{"class":"TPV","device":"/dev/ttyACM0","time":"2026-11-23T10:00:04.000Z","lat":51.477811,"lon":-0.001475}""");

        result.Error.ShouldBe(ParseErrorReason.MissingField);
        result.Class.ShouldBe(GpsdClass.Tpv);
        result.Report.ShouldBeNull();
    }

    [Theory]
    [InlineData("4")]
    [InlineData("-1")]
    [InlineData("\"3\"")]
    [InlineData("2.5")]
    public void A_tpv_with_an_unusable_mode_is_skipped_as_bad_field(string mode)
    {
        var result = Parse($$"""{"class":"TPV","mode":{{mode}}}""");

        result.Error.ShouldBe(ParseErrorReason.BadField);
        result.Report.ShouldBeNull();
    }

    [Fact]
    public void T2_5_a_satellite_without_prn_is_skipped_as_missing_field_and_the_rest_used()
    {
        var result = Parse("""{"class":"SKY","device":"/dev/ttyACM0","hdop":1.1,"satellites":[{"el":10.0,"az":20.0,"ss":30.0,"used":true},{"PRN":7,"el":45,"used":false}]}""");

        var sky = Accepted<SkyReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.MissingField, "satellites[0].PRN")]);
        var satellite = sky.Satellites.ShouldHaveSingleItem();
        satellite.Prn.ShouldBe(7);
        satellite.Used.ShouldBe(false);
        satellite.Fields[SatelliteField.El].ShouldBe(45);
        sky.Fields[SkyField.Hdop].ShouldBe(1.1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("\"7\"")]
    public void A_satellite_with_an_unusable_prn_is_skipped_as_bad_field(string prn)
    {
        var result = Parse($$"""{"class":"SKY","satellites":[{"PRN":{{prn}}}]}""");

        var sky = Accepted<SkyReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.BadField, "satellites[0].PRN")]);
        sky.Satellites.ShouldBeEmpty();
    }

    [Fact]
    public void A_bad_satellite_field_is_dropped_with_its_path()
    {
        var result = Parse("""{"class":"SKY","satellites":[{"PRN":3,"gnssid":0,"svid":3,"sigid":6,"ss":"loud","used":"yes","qual":7}]}""");

        var satellite = Accepted<SkyReport>(result).Satellites.ShouldHaveSingleItem();
        result.FieldErrors.ShouldBe(
        [
            new FieldError(ParseErrorReason.BadField, "satellites[0].ss"),
            new FieldError(ParseErrorReason.BadField, "satellites[0].used"),
        ]);
        satellite.Used.ShouldBeNull();
        satellite.Fields.Has(SatelliteField.Ss).ShouldBeFalse();
        satellite.Fields[SatelliteField.Qual].ShouldBe(7);
        (satellite.GnssId, satellite.SvId, satellite.SigId).ShouldBe((0, 3, 6));
    }

    [Fact]
    public void A_sky_without_a_satellites_array_is_dop_only()
    {
        var sky = Accepted<SkyReport>(Parse("""{"class":"SKY","hdop":1.2,"pdop":1.9}"""));

        sky.Satellites.ShouldBeNull();
        sky.Fields.PresentCount.ShouldBe(2);
    }

    [Fact]
    public void A_sky_with_an_empty_satellites_array_has_no_satellites()
    {
        var sky = Accepted<SkyReport>(Parse("""{"class":"SKY","satellites":[]}"""));

        sky.Satellites.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void A_sky_whose_satellites_is_not_an_array_keeps_its_dops()
    {
        var result = Parse("""{"class":"SKY","hdop":1.2,"satellites":{}}""");

        var sky = Accepted<SkyReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.BadField, "satellites")]);
        sky.Satellites.ShouldBeNull();
        sky.Fields[SkyField.Hdop].ShouldBe(1.2);
    }

    [Fact]
    public void T2_5_a_version_without_release_is_accepted()
    {
        var version = Accepted<VersionReport>(Parse("""{"class":"VERSION","version":"2.40dev","rev":"06f62e14","proto_major":3,"proto_minor":1}"""));

        version.Release.ShouldBeNull();
        version.ProtoMajor.ShouldBe(3);
        version.ProtoMinor.ShouldBe(1);
    }

    [Fact]
    public void T2_5_a_device_with_numeric_activated_zero_is_a_deactivation()
    {
        var device = Accepted<DeviceReport>(Parse("""{"class":"DEVICE","path":"/dev/ttyACM0","activated":0}"""));

        device.Activation.ShouldBe(DeviceActivation.Deactivated);
        device.Path.ShouldBe("/dev/ttyACM0");
        device.Device.ShouldBe("/dev/ttyACM0");
    }

    [Theory]
    [InlineData("\"activated\":\"2026-11-23T10:00:00.000Z\",", nameof(DeviceActivation.Activated))]
    [InlineData("\"activated\":1330212592.5,", nameof(DeviceActivation.Activated))]
    [InlineData("", nameof(DeviceActivation.Absent))]
    public void A_device_activation_is_read_from_activated(string activated, string expected)
    {
        var device = Accepted<DeviceReport>(Parse($$"""{"class":"DEVICE",{{activated}}"path":"/dev/ttyACM0","driver":"u-blox","bps":9600}"""));

        device.Activation.ShouldBe(Enum.Parse<DeviceActivation>(expected));
        device.Driver.ShouldBe("u-blox");
        device.Bps.ShouldBe(9600);
    }

    [Fact]
    public void Devices_lists_each_device()
    {
        var devices = Accepted<DevicesReport>(Parse("""{"class":"DEVICES","devices":[{"class":"DEVICE","path":"/dev/a","activated":"2026-11-23T10:00:00Z"},{"class":"DEVICE","path":"/dev/b"}]}"""));

        devices.Devices.Select(d => d.Path).ShouldBe(["/dev/a", "/dev/b"]);
        devices.Devices[0].Activation.ShouldBe(DeviceActivation.Activated);
    }

    [Fact]
    public void Devices_without_a_devices_array_is_missing_field()
    {
        var result = Parse("""{"class":"DEVICES"}""");

        result.Error.ShouldBe(ParseErrorReason.MissingField);
        result.Class.ShouldBe(GpsdClass.Devices);
    }

    [Theory]
    [InlineData("""{"class":"AIS","device":"/dev/ttyUSB1","type":1,"repeat":0,"mmsi":366053209}""", "AIS")]
    [InlineData("""{"class":"FOOBAR","x":1}""", "FOOBAR")]
    [InlineData("""{"class":"x7Qz9-random"}""", "x7Qz9-random")]
    [InlineData("""{"class":"tpv","mode":3}""", "tpv")]
    public void T2_6_an_unknown_class_is_other(string line, string className)
    {
        var result = Parse(line);

        result.Class.ShouldBe(GpsdClass.Other);
        result.Class!.Value.Label().ShouldBe("other");
        Accepted<OtherReport>(result).ClassName.ShouldBe(className);
    }

    [Fact]
    public void T2_6_class_labels_are_a_fixed_list()
    {
        GpsdClassExtensions.All.Select(c => c.Label()).ShouldBe(
            ["VERSION", "DEVICES", "DEVICE", "WATCH", "TPV", "SKY", "PPS", "TOFF", "OSC", "ERROR", "other"],
            ignoreOrder: true);
    }

    [Fact]
    public void Parse_error_reasons_are_a_fixed_list()
    {
        ParseErrorReasonExtensions.All.Select(r => r.Label()).ShouldBe(
            ["too_long", "invalid_json", "not_object", "missing_class", "missing_field", "bad_field", "missing_device", "too_many_devices", "duplicate_satellite"],
            ignoreOrder: true);
    }

    [Fact]
    public void Pps_keeps_integer_seconds_and_nanoseconds_and_qerr()
    {
        var pps = Accepted<TimeOffsetReport>(Parse("""{"class":"PPS","device":"/dev/pps0","real_sec":1330212592,"real_nsec":999999999,"clock_sec":1330212593,"clock_nsec":2,"precision":-20,"qErr":-1234}"""));

        (pps.Kind, pps.RealSec, pps.RealNsec, pps.ClockSec, pps.ClockNsec).ShouldBe((GpsdClass.Pps, 1_330_212_592L, 999_999_999L, 1_330_212_593L, 2L));
        pps.Precision.ShouldBe(-20);
        pps.QErrPicoseconds.ShouldBe(-1234);
    }

    [Fact]
    public void Toff_ignores_qerr()
    {
        var toff = Accepted<TimeOffsetReport>(Parse("""{"class":"TOFF","real_sec":1,"real_nsec":2,"clock_sec":3,"clock_nsec":4,"qErr":5}"""));

        toff.Kind.ShouldBe(GpsdClass.Toff);
        toff.QErrPicoseconds.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"class":"PPS","real_sec":1,"real_nsec":2,"clock_sec":3}""", nameof(ParseErrorReason.MissingField))]
    [InlineData("""{"class":"PPS","real_sec":1,"real_nsec":1000000000,"clock_sec":3,"clock_nsec":4}""", nameof(ParseErrorReason.BadField))]
    [InlineData("""{"class":"TOFF","real_sec":"1","real_nsec":2,"clock_sec":3,"clock_nsec":4}""", nameof(ParseErrorReason.BadField))]
    [InlineData("""{"class":"TOFF","real_sec":"1","real_nsec":2,"clock_sec":3}""", nameof(ParseErrorReason.MissingField))]
    public void Pps_and_toff_need_all_four_time_fields(string line, string expected)
    {
        var result = Parse(line);

        result.Error.ShouldBe(Enum.Parse<ParseErrorReason>(expected));
        result.Report.ShouldBeNull();
    }

    [Fact]
    public void Error_keeps_its_message()
    {
        var error = Accepted<ErrorReport>(Parse("""{"class":"ERROR","message":"Unrecognized request '?FOO'"}"""));

        error.Message.ShouldBe("Unrecognized request '?FOO'");
    }

    [Fact]
    public void Every_man_page_example_gives_its_expected_result()
    {
        var results = Fixtures.LoadBytes("doc-examples.jsonl").Select(l => GpsdParser.Parse(l)).ToList();

        results.Select(r => (r.Class, r.Error)).ShouldBe(
        [
            (GpsdClass.Tpv, null),
            (GpsdClass.Sky, null),
            (GpsdClass.Other, null), // GST
            (GpsdClass.Other, null), // ATT
            (GpsdClass.Toff, null),
            (GpsdClass.Pps, null),
            (GpsdClass.Osc, null),
            (GpsdClass.Version, null), // "version" instead of "release": accepted (AC2.4)
            (null, ParseErrorReason.InvalidJson), // "class"= (AC2.2)
            (GpsdClass.Watch, null),
            (GpsdClass.Other, null), // POLL
            (GpsdClass.Device, null),
            (GpsdClass.Device, null), // "activated":0
            (GpsdClass.Other, null), // ACK
            (GpsdClass.Error, null),
            (GpsdClass.Other, null), // RTCM2
            (GpsdClass.Other, null), // RTCM2
        ]);
        results.ShouldAllBe(r => r.FieldErrors.Count == 0);

        var tpv = (TpvReport)results[0].Report!;
        (tpv.Device, tpv.Mode).ShouldBe(("/dev/pts/1", 3));
        tpv.Time.ShouldBe(new DateTimeOffset(2005, 6, 8, 10, 34, 48, 283, TimeSpan.Zero));
        tpv.Fields[TpvField.Lat].ShouldBe(46.498293369);
        tpv.Fields[TpvField.Lon].ShouldBe(7.567411672);
        tpv.Fields[TpvField.Ept].ShouldBe(0.005);

        var sky = (SkyReport)results[1].Report!;
        sky.Satellites.ShouldNotBeNull().Count.ShouldBe(8);
        sky.Satellites.Count(s => s.Used == true).ShouldBe(5);

        var toff = (TimeOffsetReport)results[4].Report!;
        (toff.RealNsec, toff.ClockNsec, toff.Precision).ShouldBe((343_182L, 343_184L, -2));

        var osc = (OscReport)results[6].Report!;
        (osc.Running, osc.Reference, osc.Disciplined, osc.DeltaNanoseconds).ShouldBe((true, true, true, 67d));

        ((VersionReport)results[7].Report!).Release.ShouldBeNull();
        ((DeviceReport)results[12].Report!).Activation.ShouldBe(DeviceActivation.Deactivated);
    }

    [Fact]
    public void Every_gpsd_regression_line_is_accepted_without_field_errors()
    {
        foreach (var fixture in Fixtures.GpsdRegress())
        {
            foreach (var line in Fixtures.LoadBytes(fixture))
            {
                var result = GpsdParser.Parse(line);

                result.Error.ShouldBeNull($"{fixture}: {Encoding.UTF8.GetString(line)}");
                result.FieldErrors.ShouldBeEmpty($"{fixture}: {Encoding.UTF8.GetString(line)}");
            }
        }
    }

    [Fact]
    public void Every_synthetic_session_line_is_accepted_without_field_errors()
    {
        foreach (var fixture in Fixtures.Catalogue.Where(f => f.StartsWith("session-", StringComparison.Ordinal)))
        {
            foreach (var line in Fixtures.LoadBytes(fixture))
            {
                var result = GpsdParser.Parse(line);

                result.Error.ShouldBeNull($"{fixture}: {Encoding.UTF8.GetString(line)}");
                if (fixture == "session-multi-gnss.jsonl" && result.Class == GpsdClass.Sky)
                {
                    // The fixture ends its array with a duplicate on purpose (T6.4).
                    result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.DuplicateSatellite, "satellites[15]")]);
                }
                else
                {
                    result.FieldErrors.ShouldBeEmpty($"{fixture}: {Encoding.UTF8.GetString(line)}");
                }
            }
        }
    }

    [Fact]
    public void T6_4_a_duplicate_satellite_identity_keeps_the_last_in_the_first_ones_place()
    {
        var result = Parse("""{"class":"SKY","satellites":[{"PRN":8,"gnssid":0,"svid":8,"sigid":0,"ss":44},{"PRN":3,"gnssid":0,"svid":3,"sigid":0},{"PRN":8,"gnssid":0,"svid":8,"sigid":0,"ss":45}]}""");

        var sky = Accepted<SkyReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.DuplicateSatellite, "satellites[2]")]);
        sky.Satellites!.Select(s => (s.SvId, s.Fields[SatelliteField.Ss])).ShouldBe([(8, 45d), (3, (double?)null)]);
    }

    [Fact]
    public void T6_4_prn_only_satellites_are_duplicates_by_prn()
    {
        var result = Parse("""{"class":"SKY","satellites":[{"PRN":17,"ss":1},{"PRN":17,"ss":2},{"PRN":18}]}""");

        var sky = Accepted<SkyReport>(result);
        result.FieldErrors.ShouldBe([new FieldError(ParseErrorReason.DuplicateSatellite, "satellites[1]")]);
        sky.Satellites!.Select(s => s.Prn).ShouldBe([17, 18]);
        sky.Satellites![0].Fields[SatelliteField.Ss].ShouldBe(2);
    }

    [Fact]
    public void T6_2_the_same_satellite_on_two_signals_is_not_a_duplicate()
    {
        var result = Parse("""{"class":"SKY","satellites":[{"PRN":3,"gnssid":0,"svid":3,"sigid":0},{"PRN":3,"gnssid":0,"svid":3,"sigid":6}]}""");

        Accepted<SkyReport>(result).Satellites!.Count.ShouldBe(2);
        result.FieldErrors.ShouldBeEmpty();
    }
}
