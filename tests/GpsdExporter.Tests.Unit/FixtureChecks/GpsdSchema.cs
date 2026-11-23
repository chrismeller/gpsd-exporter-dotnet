using System.Text.Json;

namespace GpsdExporter.Tests.Unit.FixtureChecks;

/// <summary>
/// The object tables of gpsd_json(5), as data: field name, JSON type, whether the field is
/// always present, and a value range where the man page gives one. Used only to check that
/// the synthetic fixtures are faithful to the man page; the exporter has its own parser.
/// </summary>
internal static class GpsdSchema
{
    private enum Kind { Number, Integer, String, Bool, Array, Object }

    private sealed record Field(Kind Kind, bool Required = false, double Min = double.NegativeInfinity, double Max = double.PositiveInfinity);

    private static readonly Field Num = new(Kind.Number);
    private static readonly Field Str = new(Kind.String);

    private static readonly Dictionary<string, Field> Tpv = new()
    {
        ["class"] = new(Kind.String, true), ["device"] = Str, ["mode"] = new(Kind.Integer, true, 0, 3),
        ["alt"] = Num, ["altHAE"] = Num, ["altMSL"] = Num, ["ant"] = new(Kind.Integer, false, 0, 3),
        ["antPwr"] = new(Kind.Integer, false, 0, 2), ["climb"] = Num, ["clockbias"] = Num, ["clockdrift"] = Num,
        ["datum"] = Str, ["depth"] = Num, ["dgpsAge"] = Num, ["dgpsSta"] = Num,
        ["ecefx"] = Num, ["ecefy"] = Num, ["ecefz"] = Num, ["ecefpAcc"] = Num,
        ["ecefvx"] = Num, ["ecefvy"] = Num, ["ecefvz"] = Num, ["ecefvAcc"] = Num,
        ["epc"] = Num, ["epd"] = Num, ["eph"] = Num, ["eps"] = Num, ["ept"] = Num, ["epx"] = Num, ["epy"] = Num, ["epv"] = Num,
        ["geoidSep"] = Num, ["jam"] = new(Kind.Integer, false, -1, 255),
        ["lat"] = new(Kind.Number, false, -90, 90), ["leapseconds"] = new(Kind.Integer),
        ["lon"] = new(Kind.Number, false, -180, 180), ["magdev"] = Num,
        ["magtrack"] = new(Kind.Number, false, 0, 360), ["magvar"] = Num,
        ["relD"] = Num, ["relE"] = Num, ["relN"] = Num, ["sep"] = Num, ["speed"] = Num,
        ["status"] = new(Kind.Integer, false, 0, 9), ["temp"] = Num, ["time"] = Str,
        ["track"] = new(Kind.Number, false, 0, 360), ["velD"] = Num, ["velE"] = Num, ["velN"] = Num,
        ["wanglem"] = Num, ["wangler"] = Num, ["wanglet"] = Num, ["wspeedr"] = Num, ["wspeedt"] = Num, ["wtemp"] = Num,
    };

    private static readonly Dictionary<string, Field> Sky = new()
    {
        ["class"] = new(Kind.String, true), ["device"] = Str, ["nSat"] = new(Kind.Integer, false, 0),
        ["gdop"] = Num, ["hdop"] = Num, ["pdop"] = Num, ["tdop"] = Num, ["vdop"] = Num, ["xdop"] = Num, ["ydop"] = Num,
        ["pr"] = Num, ["prRate"] = Num, ["prRes"] = Num, ["qual"] = new(Kind.Integer, false, 0, 7),
        ["satellites"] = new(Kind.Array), ["time"] = Str, ["uSat"] = new(Kind.Integer, false, 0),
    };

    private static readonly Dictionary<string, Field> Satellite = new()
    {
        ["PRN"] = new(Kind.Integer, true, 1), ["az"] = new(Kind.Number, false, 0, 360), ["el"] = new(Kind.Number, false, -90, 90),
        ["freqid"] = new(Kind.Integer, false, 0, 13), ["gnssid"] = new(Kind.Integer, false, 0, 7),
        ["health"] = new(Kind.Integer, false, 0, 2), ["pr"] = Num, ["prRate"] = Num, ["prRes"] = Num,
        ["qual"] = new(Kind.Integer, false, 0, 7), ["ss"] = new(Kind.Number, false, 0),
        ["sigid"] = new(Kind.Integer, false, 0), ["svid"] = new(Kind.Integer, false, 0), ["used"] = new(Kind.Bool, true),
    };

    private static readonly Dictionary<string, Field> Toff = new()
    {
        ["class"] = new(Kind.String, true), ["device"] = new(Kind.String, true),
        ["real_sec"] = new(Kind.Integer, true, 0), ["real_nsec"] = new(Kind.Integer, true, 0, 999_999_999),
        ["clock_sec"] = new(Kind.Integer, true, 0), ["clock_nsec"] = new(Kind.Integer, true, 0, 999_999_999),
        ["precision"] = new(Kind.Integer),
    };

    private static readonly Dictionary<string, Field> Pps = new(Toff)
    {
        ["precision"] = new(Kind.Integer, true), ["shm"] = new(Kind.String, true), ["qErr"] = new(Kind.Integer),
    };

    private static readonly Dictionary<string, Field> Osc = new()
    {
        ["class"] = new(Kind.String, true), ["device"] = new(Kind.String, true), ["running"] = new(Kind.Bool, true),
        ["reference"] = new(Kind.Bool, true), ["disciplined"] = new(Kind.Bool, true), ["delta"] = new(Kind.Integer, true),
    };

    private static readonly Dictionary<string, Field> Version = new()
    {
        ["class"] = new(Kind.String, true), ["release"] = new(Kind.String, true), ["rev"] = new(Kind.String, true),
        ["proto_major"] = new(Kind.Integer, true), ["proto_minor"] = new(Kind.Integer, true), ["remote"] = Str,
    };

    private static readonly Dictionary<string, Field> Device = new()
    {
        ["class"] = new(Kind.String, true), ["activated"] = Str, ["bps"] = new(Kind.Integer), ["cycle"] = Num,
        ["driver"] = Str, ["flags"] = new(Kind.Integer), ["hexdata"] = Str, ["mincycle"] = Num,
        ["native"] = new(Kind.Integer, false, 0, 1), ["parity"] = Str, ["path"] = Str, ["readonly"] = new(Kind.Bool),
        ["sernum"] = Str, ["stopbits"] = new(Kind.Integer, false, 1, 2), ["subtype"] = Str, ["subtype1"] = Str,
    };

    private static readonly Dictionary<string, Field> Devices = new()
    {
        ["class"] = new(Kind.String, true), ["devices"] = new(Kind.Array, true), ["remote"] = Str,
    };

    private static readonly Dictionary<string, Field> Watch = new()
    {
        ["class"] = new(Kind.String, true), ["enable"] = new(Kind.Bool), ["json"] = new(Kind.Bool), ["nmea"] = new(Kind.Bool),
        ["raw"] = new(Kind.Integer), ["scaled"] = new(Kind.Bool), ["split24"] = new(Kind.Bool), ["pps"] = new(Kind.Bool),
        ["timing"] = new(Kind.Bool), ["device"] = Str, ["remote"] = Str,
    };

    public static void Check(JsonElement obj, List<string> problems)
    {
        var cls = obj.GetProperty("class").GetString();
        switch (cls)
        {
            case "TPV": CheckObject("TPV", obj, Tpv, problems); break;
            case "SKY":
                CheckObject("SKY", obj, Sky, problems);
                if (obj.TryGetProperty("satellites", out var sats))
                {
                    foreach (var s in sats.EnumerateArray())
                    {
                        CheckObject("SKY.satellites[]", s, Satellite, problems);
                    }
                }

                break;
            case "TOFF": CheckObject("TOFF", obj, Toff, problems); break;
            case "PPS": CheckObject("PPS", obj, Pps, problems); break;
            case "OSC": CheckObject("OSC", obj, Osc, problems); break;
            case "VERSION": CheckObject("VERSION", obj, Version, problems); break;
            case "DEVICE": CheckObject("DEVICE", obj, Device, problems); break;
            case "WATCH": CheckObject("WATCH", obj, Watch, problems); break;
            case "DEVICES":
                CheckObject("DEVICES", obj, Devices, problems);
                foreach (var d in obj.GetProperty("devices").EnumerateArray())
                {
                    CheckObject("DEVICES.devices[]", d, Device, problems);
                }

                break;
            default: problems.Add($"unexpected class {cls} in a synthetic session"); break;
        }
    }

    private static void CheckObject(string what, JsonElement obj, Dictionary<string, Field> table, List<string> problems)
    {
        foreach (var (name, field) in table)
        {
            if (field.Required && !obj.TryGetProperty(name, out _))
            {
                problems.Add($"{what}: required field '{name}' missing in {obj.GetRawText()}");
            }
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (!table.TryGetValue(prop.Name, out var field))
            {
                problems.Add($"{what}: field '{prop.Name}' is not in the man page table");
                continue;
            }

            var v = prop.Value;
            var kindOk = field.Kind switch
            {
                Kind.Number => v.ValueKind == JsonValueKind.Number,
                Kind.Integer => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _),
                Kind.String => v.ValueKind == JsonValueKind.String,
                Kind.Bool => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
                Kind.Array => v.ValueKind == JsonValueKind.Array,
                Kind.Object => v.ValueKind == JsonValueKind.Object,
                _ => false,
            };

            // DEVICE "activated" is documented as a string, but the daemon sends a number 0
            // on deactivation notices (spec AC2.3). Both forms are legitimate fixture content.
            if (!kindOk && what.StartsWith("DEVICE", StringComparison.Ordinal) && prop.Name == "activated"
                && v.ValueKind == JsonValueKind.Number)
            {
                kindOk = true;
            }

            if (!kindOk)
            {
                problems.Add($"{what}.{prop.Name}: expected {field.Kind}, got {v.ValueKind}");
                continue;
            }

            if (v.ValueKind == JsonValueKind.Number)
            {
                var d = v.GetDouble();
                if (d < field.Min || d > field.Max)
                {
                    problems.Add($"{what}.{prop.Name}: {d} outside [{field.Min}, {field.Max}]");
                }
            }

            if (prop.Name == "time" && v.ValueKind == JsonValueKind.String
                && !DateTimeOffset.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out _))
            {
                problems.Add($"{what}.time: '{v.GetString()}' is not ISO 8601");
            }
        }
    }
}
