using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;

namespace GpsdExporter.Protocol;

/// <summary>
/// Parses one gpsd line into a typed report (spec R2, AC7.5).
/// </summary>
/// <remarks>
/// <para>
/// JSON is read with <see cref="JsonDocument"/>, field by field. There is no reflection-based
/// deserialisation, which is what allows one bad field to be dropped on its own (AC2.3), and
/// what Native AOT needs.
/// </para>
/// <para>
/// Line-level problems reject the line: invalid JSON or UTF-8, a value that is not an object,
/// and a missing or non-string <c>class</c> (AC2.2). A field with the wrong JSON type or an
/// unusable value (non-finite, out of range, unparseable time) is dropped and reported as
/// <see cref="ParseErrorReason.BadField"/>, and the rest of the object is used (AC2.3). A TPV
/// without <c>mode</c>, a PPS or TOFF without its time fields, and a satellite without
/// <c>PRN</c> are skipped as <see cref="ParseErrorReason.MissingField"/> (AC2.4); a required
/// field that is present but unusable skips the object as <see cref="ParseErrorReason.BadField"/>.
/// Two satellites with the same <see cref="SatelliteId"/> in one SKY keep the last and report
/// <see cref="ParseErrorReason.DuplicateSatellite"/> (AC6.5).
/// </para>
/// <para>The parser reports; it does not log or count.</para>
/// </remarks>
internal static class GpsdParser
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    };

    private static readonly (string Name, TpvField Field)[] TpvFields =
    [
        ("status", TpvField.Status), ("leapseconds", TpvField.LeapSeconds),
        ("lat", TpvField.Lat), ("lon", TpvField.Lon),
        ("altHAE", TpvField.AltHae), ("altMSL", TpvField.AltMsl), ("geoidSep", TpvField.GeoidSep),
        ("speed", TpvField.Speed), ("climb", TpvField.Climb),
        ("track", TpvField.Track), ("magtrack", TpvField.MagTrack), ("magvar", TpvField.MagVar),
        ("velN", TpvField.VelN), ("velE", TpvField.VelE), ("velD", TpvField.VelD),
        ("ecefx", TpvField.EcefX), ("ecefy", TpvField.EcefY), ("ecefz", TpvField.EcefZ),
        ("ecefvx", TpvField.EcefVx), ("ecefvy", TpvField.EcefVy), ("ecefvz", TpvField.EcefVz),
        ("ecefpAcc", TpvField.EcefPAcc), ("ecefvAcc", TpvField.EcefVAcc),
        ("relN", TpvField.RelN), ("relE", TpvField.RelE), ("relD", TpvField.RelD),
        ("eph", TpvField.Eph), ("epv", TpvField.Epv), ("epy", TpvField.Epy), ("epx", TpvField.Epx),
        ("sep", TpvField.Sep), ("ept", TpvField.Ept), ("eps", TpvField.Eps), ("epc", TpvField.Epc), ("epd", TpvField.Epd),
        ("clockbias", TpvField.ClockBias), ("clockdrift", TpvField.ClockDrift),
        ("dgpsAge", TpvField.DgpsAge), ("dgpsSta", TpvField.DgpsSta),
        ("jam", TpvField.Jam), ("ant", TpvField.Ant), ("antPwr", TpvField.AntPwr), ("temp", TpvField.Temp),
    ];

    private static readonly (string Name, SkyField Field)[] SkyFields =
    [
        ("gdop", SkyField.Gdop), ("hdop", SkyField.Hdop), ("pdop", SkyField.Pdop), ("tdop", SkyField.Tdop),
        ("vdop", SkyField.Vdop), ("xdop", SkyField.Xdop), ("ydop", SkyField.Ydop),
        ("nSat", SkyField.NSat), ("uSat", SkyField.USat),
    ];

    private static readonly (string Name, SatelliteField Field)[] SatelliteFields =
    [
        ("az", SatelliteField.Az), ("el", SatelliteField.El), ("ss", SatelliteField.Ss),
        ("health", SatelliteField.Health), ("qual", SatelliteField.Qual), ("prRes", SatelliteField.PrRes),
    ];

    public static ParseResult Parse(ReadOnlySpan<byte> line)
    {
        // gpsd sends US-ASCII; anything that is not even valid UTF-8 is garbage (AC2.2).
        if (!Utf8.IsValid(line))
        {
            return ParseResult.Rejected(ParseErrorReason.InvalidJson);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line.ToArray(), Options);
        }
        catch (JsonException)
        {
            return ParseResult.Rejected(ParseErrorReason.InvalidJson);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ParseResult.Rejected(ParseErrorReason.NotObject);
            }

            if (!root.TryGetProperty("class", out var classElement) || classElement.ValueKind != JsonValueKind.String)
            {
                return ParseResult.Rejected(ParseErrorReason.MissingClass);
            }

            var className = classElement.GetString()!;
            var cls = GpsdClassExtensions.FromName(className);
            var reader = new ObjectReader();
            return cls switch
            {
                GpsdClass.Tpv => ParseTpv(root, reader),
                GpsdClass.Sky => ParseSky(root, reader),
                GpsdClass.Pps or GpsdClass.Toff => ParseTimeOffset(root, cls, reader),
                GpsdClass.Osc => reader.Accept(cls, new OscReport(
                    reader.String(root, "device"),
                    reader.Bool(root, "running"),
                    reader.Bool(root, "reference"),
                    reader.Bool(root, "disciplined"),
                    reader.Number(root, "delta"))),
                GpsdClass.Version => reader.Accept(cls, new VersionReport(
                    reader.String(root, "release"),
                    reader.String(root, "rev"),
                    reader.Int(root, "proto_major"),
                    reader.Int(root, "proto_minor"))),
                GpsdClass.Device => reader.Accept(cls, ParseDevice(root, reader, string.Empty)),
                GpsdClass.Devices => ParseDevices(root, reader),
                GpsdClass.Watch => reader.Accept(cls, new WatchReport(reader.String(root, "device"))),
                GpsdClass.Error => reader.Accept(cls, new ErrorReport(reader.String(root, "message"))),
                _ => reader.Accept(GpsdClass.Other, new OtherReport(className)),
            };
        }
    }

    private static ParseResult ParseTpv(JsonElement obj, ObjectReader reader)
    {
        switch (ObjectReader.RequiredInt(obj, "mode", min: 0, max: 3, out var mode))
        {
            case Required.Missing: return reader.Reject(GpsdClass.Tpv, ParseErrorReason.MissingField);
            case Required.Bad: return reader.Reject(GpsdClass.Tpv, ParseErrorReason.BadField);
        }

        var device = reader.String(obj, "device");
        var time = reader.Time(obj, "time");
        var fields = new FieldValues<TpvField>.Builder();
        foreach (var (name, field) in TpvFields)
        {
            var value = field switch
            {
                TpvField.Lat => reader.Number(obj, name, -90, 90),
                TpvField.Lon => reader.Number(obj, name, -180, 180),
                _ => reader.Number(obj, name),
            };

            // jam -1 means "unset" (gpsd_json(5)), so it is treated as absent, not as an error.
            if (value is { } v && !(field == TpvField.Jam && v == -1))
            {
                fields.Set(field, v);
            }
        }

        return reader.Accept(GpsdClass.Tpv, new TpvReport(device, mode, time, fields.Build()));
    }

    private static ParseResult ParseSky(JsonElement obj, ObjectReader reader)
    {
        var device = reader.String(obj, "device");
        var time = reader.Time(obj, "time");
        var fields = new FieldValues<SkyField>.Builder();
        foreach (var (name, field) in SkyFields)
        {
            if (reader.Number(obj, name) is { } v)
            {
                fields.Set(field, v);
            }
        }

        List<Satellite>? satellites = null;
        if (obj.TryGetProperty("satellites", out var array))
        {
            if (array.ValueKind != JsonValueKind.Array)
            {
                reader.FieldError(ParseErrorReason.BadField, "satellites");
            }
            else
            {
                satellites = [];
                Dictionary<SatelliteId, int>? positions = null;
                var index = 0;
                foreach (var element in array.EnumerateArray())
                {
                    if (ParseSatellite(element, $"satellites[{index}]", reader) is { } satellite)
                    {
                        // AC6.5: the last satellite with an identity wins, in the earlier one's place,
                        // so the array never yields two series with the same labels.
                        positions ??= [];
                        var id = SatelliteId.Of(satellite);
                        if (positions.TryGetValue(id, out var earlier))
                        {
                            reader.FieldError(ParseErrorReason.DuplicateSatellite, $"satellites[{index}]");
                            satellites[earlier] = satellite;
                        }
                        else
                        {
                            positions.Add(id, satellites.Count);
                            satellites.Add(satellite);
                        }
                    }

                    index++;
                }
            }
        }

        return reader.Accept(GpsdClass.Sky, new SkyReport(device, time, fields.Build(), satellites));
    }

    private static Satellite? ParseSatellite(JsonElement element, string path, ObjectReader reader)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            reader.FieldError(ParseErrorReason.BadField, path);
            return null;
        }

        switch (ObjectReader.RequiredInt(element, "PRN", min: 1, max: int.MaxValue, out var prn))
        {
            case Required.Missing:
                reader.FieldError(ParseErrorReason.MissingField, path + ".PRN");
                return null;
            case Required.Bad:
                reader.FieldError(ParseErrorReason.BadField, path + ".PRN");
                return null;
        }

        var fields = new FieldValues<SatelliteField>.Builder();
        foreach (var (name, field) in SatelliteFields)
        {
            if (reader.Number(element, name, fieldPath: $"{path}.{name}") is { } v)
            {
                fields.Set(field, v);
            }
        }

        return new Satellite(
            prn,
            reader.Int(element, "gnssid", $"{path}.gnssid"),
            reader.Int(element, "svid", $"{path}.svid"),
            reader.Int(element, "sigid", $"{path}.sigid"),
            reader.Bool(element, "used", $"{path}.used"),
            fields.Build());
    }

    private static ParseResult ParseTimeOffset(JsonElement obj, GpsdClass cls, ObjectReader reader)
    {
        var worst = Required.Ok;
        var realSec = Need(ObjectReader.RequiredLong(obj, "real_sec", 0, long.MaxValue, out var rs));
        var realNsec = Need(ObjectReader.RequiredLong(obj, "real_nsec", 0, 999_999_999, out var rn));
        var clockSec = Need(ObjectReader.RequiredLong(obj, "clock_sec", 0, long.MaxValue, out var cs));
        var clockNsec = Need(ObjectReader.RequiredLong(obj, "clock_nsec", 0, 999_999_999, out var cn));
        _ = realSec && realNsec && clockSec && clockNsec;

        if (worst != Required.Ok)
        {
            return reader.Reject(cls, worst == Required.Missing ? ParseErrorReason.MissingField : ParseErrorReason.BadField);
        }

        return reader.Accept(cls, new TimeOffsetReport(
            reader.String(obj, "device"),
            cls,
            rs,
            rn,
            cs,
            cn,
            reader.Int(obj, "precision"),
            cls == GpsdClass.Pps ? reader.Long(obj, "qErr") : null));

        bool Need(Required r)
        {
            // Missing outranks bad when both happen, as the object is unusable either way.
            if (r == Required.Missing || (r == Required.Bad && worst == Required.Ok))
            {
                worst = r;
            }

            return r == Required.Ok;
        }
    }

    private static ParseResult ParseDevices(JsonElement obj, ObjectReader reader)
    {
        if (!obj.TryGetProperty("devices", out var array))
        {
            return reader.Reject(GpsdClass.Devices, ParseErrorReason.MissingField);
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return reader.Reject(GpsdClass.Devices, ParseErrorReason.BadField);
        }

        var devices = new List<DeviceReport>();
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                devices.Add(ParseDevice(element, reader, $"devices[{index}]."));
            }
            else
            {
                reader.FieldError(ParseErrorReason.BadField, $"devices[{index}]");
            }

            index++;
        }

        return reader.Accept(GpsdClass.Devices, new DevicesReport(devices));
    }

    private static DeviceReport ParseDevice(JsonElement obj, ObjectReader reader, string prefix)
    {
        var activation = DeviceActivation.Absent;
        if (obj.TryGetProperty("activated", out var activated))
        {
            switch (activated.ValueKind)
            {
                case JsonValueKind.String:
                    activation = DeviceActivation.Activated;
                    break;

                // The spec says string, but the daemon sends the number 0 on deactivation, and
                // older releases sent a numeric timestamp when active. Both are handled (AC2.3).
                case JsonValueKind.Number when activated.TryGetDouble(out var n) && double.IsFinite(n):
                    activation = n == 0 ? DeviceActivation.Deactivated : DeviceActivation.Activated;
                    break;

                default:
                    reader.FieldError(ParseErrorReason.BadField, prefix + "activated");
                    break;
            }
        }

        return new DeviceReport(
            reader.String(obj, "path", prefix + "path"),
            activation,
            reader.String(obj, "driver", prefix + "driver"),
            reader.String(obj, "subtype", prefix + "subtype"),
            reader.Number(obj, "cycle", fieldPath: prefix + "cycle"),
            reader.Number(obj, "bps", fieldPath: prefix + "bps"));
    }

    private enum Required
    {
        Ok,
        Missing,
        Bad,
    }

    /// <summary>Reads typed fields from one object and collects the field-level errors.</summary>
    private sealed class ObjectReader
    {
        private List<FieldError>? _errors;

        public IReadOnlyList<FieldError> Errors => _errors ?? [];

        public void FieldError(ParseErrorReason reason, string field) => (_errors ??= []).Add(new FieldError(reason, field));

        public ParseResult Accept(GpsdClass cls, GpsdReport report) => new(cls, report, null, Errors);

        public ParseResult Reject(GpsdClass cls, ParseErrorReason reason) => ParseResult.Rejected(reason, cls, Errors);

        public string? String(JsonElement obj, string name, string? fieldPath = null)
        {
            if (!obj.TryGetProperty(name, out var e))
            {
                return null;
            }

            if (e.ValueKind == JsonValueKind.String)
            {
                return e.GetString();
            }

            FieldError(ParseErrorReason.BadField, fieldPath ?? name);
            return null;
        }

        public bool? Bool(JsonElement obj, string name, string? fieldPath = null)
        {
            if (!obj.TryGetProperty(name, out var e))
            {
                return null;
            }

            switch (e.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default:
                    FieldError(ParseErrorReason.BadField, fieldPath ?? name);
                    return null;
            }
        }

        public double? Number(JsonElement obj, string name, double min = double.MinValue, double max = double.MaxValue, string? fieldPath = null)
        {
            if (!obj.TryGetProperty(name, out var e))
            {
                return null;
            }

            if (TryFinite(e, out var value) && value >= min && value <= max)
            {
                return value;
            }

            FieldError(ParseErrorReason.BadField, fieldPath ?? name);
            return null;
        }

        public int? Int(JsonElement obj, string name, string? fieldPath = null)
        {
            if (!obj.TryGetProperty(name, out var e))
            {
                return null;
            }

            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var value))
            {
                return value;
            }

            FieldError(ParseErrorReason.BadField, fieldPath ?? name);
            return null;
        }

        public long? Long(JsonElement obj, string name, string? fieldPath = null)
        {
            if (!obj.TryGetProperty(name, out var e))
            {
                return null;
            }

            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var value))
            {
                return value;
            }

            FieldError(ParseErrorReason.BadField, fieldPath ?? name);
            return null;
        }

        public static Required RequiredInt(JsonElement obj, string name, int min, int max, out int value)
        {
            value = 0;
            if (!obj.TryGetProperty(name, out var e))
            {
                return Required.Missing;
            }

            return e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out value) && value >= min && value <= max
                ? Required.Ok
                : Required.Bad;
        }

        public static Required RequiredLong(JsonElement obj, string name, long min, long max, out long value)
        {
            value = 0;
            if (!obj.TryGetProperty(name, out var e))
            {
                return Required.Missing;
            }

            return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out value) && value >= min && value <= max
                ? Required.Ok
                : Required.Bad;
        }

        /// <summary>
        /// An ISO 8601 UTC time string, as gpsd sends. Older gpsd releases sent seconds since
        /// the epoch as a number, which is accepted too.
        /// </summary>
        public DateTimeOffset? Time(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var e))
            {
                return null;
            }

            if (e.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }

            if (TryFinite(e, out var seconds) && seconds >= 0 && seconds < 253_402_300_800)
            {
                return DateTimeOffset.UnixEpoch.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            }

            FieldError(ParseErrorReason.BadField, name);
            return null;
        }

        private static bool TryFinite(JsonElement e, out double value)
        {
            value = 0;
            return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out value) && double.IsFinite(value);
        }
    }
}
