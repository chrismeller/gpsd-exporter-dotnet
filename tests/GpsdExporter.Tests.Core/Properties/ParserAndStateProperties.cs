using System.Globalization;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using GpsdExporter.Testing;

namespace GpsdExporter.Tests.Core.Properties;

/// <summary>
/// FsCheck properties, run from ordinary xUnit tests with a fixed run count. A failure
/// throws with FsCheck's report: the shrunk counterexample and the seed to replay it.
/// </summary>
public sealed class ParserAndStateProperties
{
    private const int Runs = 500;

    private static Config Config => Config.QuickThrowOnFailure.WithMaxTest(Runs);

    // The TPV, SKY and satellite tables of gpsd_json(5): JSON name, field, and a generator of
    // valid values. Built here independently of the parser's own table.
    private static readonly (string Name, TpvField Field, Gen<double> Values)[] TpvTable =
    [
        ("status", TpvField.Status, Ints(2, 9)), ("leapseconds", TpvField.LeapSeconds, Ints(0, 40)),
        ("lat", TpvField.Lat, Decimals(-90, 90)), ("lon", TpvField.Lon, Decimals(-180, 180)),
        ("altHAE", TpvField.AltHae, Decimals(-500, 20_000)), ("altMSL", TpvField.AltMsl, Decimals(-500, 20_000)),
        ("geoidSep", TpvField.GeoidSep, Decimals(-120, 120)),
        ("speed", TpvField.Speed, Decimals(0, 500)), ("climb", TpvField.Climb, Decimals(-100, 100)),
        ("track", TpvField.Track, Decimals(0, 360)), ("magtrack", TpvField.MagTrack, Decimals(0, 360)),
        ("magvar", TpvField.MagVar, Decimals(-180, 180)),
        ("velN", TpvField.VelN, Decimals(-500, 500)), ("velE", TpvField.VelE, Decimals(-500, 500)), ("velD", TpvField.VelD, Decimals(-500, 500)),
        ("ecefx", TpvField.EcefX, Decimals(-7e6, 7e6)), ("ecefy", TpvField.EcefY, Decimals(-7e6, 7e6)), ("ecefz", TpvField.EcefZ, Decimals(-7e6, 7e6)),
        ("ecefvx", TpvField.EcefVx, Decimals(-500, 500)), ("ecefvy", TpvField.EcefVy, Decimals(-500, 500)), ("ecefvz", TpvField.EcefVz, Decimals(-500, 500)),
        ("ecefpAcc", TpvField.EcefPAcc, Decimals(0, 1000)), ("ecefvAcc", TpvField.EcefVAcc, Decimals(0, 1000)),
        ("relN", TpvField.RelN, Decimals(-1e4, 1e4)), ("relE", TpvField.RelE, Decimals(-1e4, 1e4)), ("relD", TpvField.RelD, Decimals(-1e4, 1e4)),
        ("eph", TpvField.Eph, Decimals(0, 1000)), ("epv", TpvField.Epv, Decimals(0, 1000)), ("epy", TpvField.Epy, Decimals(0, 1000)),
        ("epx", TpvField.Epx, Decimals(0, 1000)), ("sep", TpvField.Sep, Decimals(0, 1000)), ("ept", TpvField.Ept, Decimals(0, 1)),
        ("eps", TpvField.Eps, Decimals(0, 100)), ("epc", TpvField.Epc, Decimals(0, 100)), ("epd", TpvField.Epd, Decimals(0, 360)),
        ("clockbias", TpvField.ClockBias, Decimals(-1e9, 1e9)), ("clockdrift", TpvField.ClockDrift, Decimals(-1e6, 1e6)),
        ("dgpsAge", TpvField.DgpsAge, Decimals(0, 3600)), ("dgpsSta", TpvField.DgpsSta, Ints(0, 1023)),
        ("jam", TpvField.Jam, Ints(0, 255)), ("ant", TpvField.Ant, Ints(0, 3)), ("antPwr", TpvField.AntPwr, Ints(0, 3)),
        ("temp", TpvField.Temp, Decimals(-40, 85)),
    ];

    private static readonly (string Name, SkyField Field, Gen<double> Values)[] SkyTable =
    [
        ("gdop", SkyField.Gdop, Decimals(0, 99)), ("hdop", SkyField.Hdop, Decimals(0, 99)), ("pdop", SkyField.Pdop, Decimals(0, 99)),
        ("tdop", SkyField.Tdop, Decimals(0, 99)), ("vdop", SkyField.Vdop, Decimals(0, 99)), ("xdop", SkyField.Xdop, Decimals(0, 99)),
        ("ydop", SkyField.Ydop, Decimals(0, 99)), ("nSat", SkyField.NSat, Ints(0, 200)), ("uSat", SkyField.USat, Ints(0, 200)),
    ];

    private static readonly (string Name, SatelliteField Field, Gen<double> Values)[] SatelliteTable =
    [
        ("az", SatelliteField.Az, Decimals(0, 359)), ("el", SatelliteField.El, Decimals(-10, 90)), ("ss", SatelliteField.Ss, Decimals(0, 60)),
        ("health", SatelliteField.Health, Ints(0, 2)), ("qual", SatelliteField.Qual, Ints(0, 7)), ("prRes", SatelliteField.PrRes, Decimals(-100, 100)),
    ];

    private static readonly string[] DevicePaths = ["/dev/ttyACM0", "/dev/ttyUSB0", "/dev/ttyUSB1"];

    private static Gen<double> Ints(int min, int max) => Gen.Choose(min, max).Select(i => (double)i);

    /// <summary>Values with up to six decimals, which survive a JSON round trip exactly.</summary>
    private static Gen<double> Decimals(double min, double max) =>
        Gen.Choose((int)Math.Ceiling(min * 1000), (int)Math.Floor(max * 1000))
            .SelectMany(milli => Gen.Choose(0, 999).Select(micro => Math.Clamp(((milli * 1000.0) + (milli < 0 ? -micro : micro)) / 1e6, min, max)));

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Each entry of the table, present with a value or absent.</summary>
    private static Gen<(TField Field, string Name, double? Value)[]> Present<TField>((string Name, TField Field, Gen<double> Values)[] table) =>
        Gen.CollectToArray(table.Select(row =>
            Gen.Frequency(
                (1, Gen.Constant<(TField, string, double?)>((row.Field, row.Name, null))),
                (2, row.Values.Select(v => (row.Field, row.Name, (double?)v))))))
        .Select(rows => rows.ToArray());

    private static string Members<TField>(IEnumerable<(TField Field, string Name, double? Value)> fields) =>
        string.Concat(fields.Where(f => f.Value is not null).Select(f => $",\"{f.Name}\":{Number(f.Value!.Value)}"));

    // ---------------------------------------------------------------------------------------
    // (a) The parser never throws.

    private static readonly byte[][] FixtureLines =
        [.. Fixtures.All().SelectMany(Fixtures.LoadBytes).Where(l => l.Length <= 10_240)];

    private static Gen<byte[]> Mutated =>
        from line in Gen.Elements(FixtureLines)
        from mutations in Gen.Choose(1, 8)
        from steps in Gen.ArrayOf(Gen.Zip(Gen.Zip(Gen.Choose(0, 2), Gen.Choose(0, int.MaxValue)), Gen.Choose(0, 255), (a, b) => (a.Item1, a.Item2, b)), mutations)
        select Mutate(line, steps);

    private static byte[] Mutate(byte[] line, (int Kind, int Position, int Value)[] steps)
    {
        var bytes = new List<byte>(line);
        foreach (var (kind, position, value) in steps)
        {
            if (bytes.Count == 0)
            {
                bytes.Add((byte)value);
                continue;
            }

            var at = position % bytes.Count;
            switch (kind)
            {
                case 0:
                    bytes[at] ^= (byte)(1 << (value % 8));
                    break;
                case 1:
                    bytes.RemoveAt(at);
                    break;
                default:
                    bytes.Insert(at, (byte)value);
                    break;
            }
        }

        return [.. bytes];
    }

    [Fact]
    public void A_the_parser_never_throws_on_arbitrary_bytes()
    {
        Check.One(
            Config.WithMaxTest(Runs * 4),
            Prop.ForAll(ArbMap.Default.ArbFor<byte[]>(), bytes => { _ = GpsdParser.Parse(bytes); }));
    }

    [Fact]
    public void A_the_parser_never_throws_on_mutated_fixture_lines()
    {
        Check.One(
            Config.WithMaxTest(Runs * 4),
            Prop.ForAll(Mutated.ToArbitrary(), bytes =>
            {
                var result = GpsdParser.Parse(bytes);
                (result.Report is null).ShouldBe(result.Error is not null);
            }));
    }

    // ---------------------------------------------------------------------------------------
    // (b) Every present field is in state with exactly its value; every omitted field is not.

    private static Gen<(int Mode, (TpvField Field, string Name, double? Value)[] Fields)> Tpvs =>
        from mode in Gen.Choose(0, 3)
        from fields in Present(TpvTable)
        select (mode, fields);

    [Fact]
    public void B_a_generated_tpv_produces_exactly_its_fields()
    {
        Check.One(Config, Prop.ForAll(Tpvs.ToArbitrary(), tpv =>
        {
            var (mode, fields) = tpv;
            var line = $$"""{"class":"TPV","device":"/dev/ttyACM0","mode":{{mode}},"time":"2026-11-23T10:00:00.000Z"{{Members(fields)}}}""";
            var parsed = GpsdParser.Parse(Encoding.UTF8.GetBytes(line));
            parsed.Error.ShouldBeNull(line);
            parsed.FieldErrors.ShouldBeEmpty(line);

            var store = new StateStore(TimeProvider.System);
            store.Apply(parsed.Report!).ShouldBeNull();
            var state = store.Current.Devices["/dev/ttyACM0"].Tpv!;

            state.Mode.Value.ShouldBe(mode);
            foreach (var (field, name, value) in fields)
            {
                var allowed = mode switch
                {
                    < 2 => !TpvState.FixFields.Contains(field) && !TpvState.ThreeDFields.Contains(field),
                    2 => !TpvState.ThreeDFields.Contains(field),
                    _ => true,
                };
                (state[field]?.Value).ShouldBe(allowed ? value : null, $"{name} in {line}");
            }

            state.Served.Count().ShouldBe(fields.Count(f => state[f.Field] is not null));
        }));
    }

    private static Gen<(int? Gnss, int? Sv, int? Sig, int Prn)> Identities =>
        Gen.OneOf(
            Gen.Choose(1, 500).Select(prn => ((int?)null, (int?)null, (int?)null, prn)),
            from gnss in Gen.Choose(0, 7)
            from sv in Gen.Choose(1, 40)
            from sig in Gen.Choose(0, 8)
            select ((int?)gnss, (int?)sv, (int?)sig, (gnss * 100) + sv));

    private static Gen<SatelliteSpec> Satellites =>
        from id in Identities
        from used in Gen.Elements<bool?>(true, false, null)
        from fields in Present(SatelliteTable)
        select new SatelliteSpec(id.Gnss, id.Sv, id.Sig, id.Prn, used, fields);

    private sealed record SatelliteSpec(int? Gnss, int? Sv, int? Sig, int Prn, bool? Used, (SatelliteField Field, string Name, double? Value)[] Fields)
    {
        public (int?, int?, int?, int?) Key => Gnss is not null && Sv is not null ? (Gnss, Sv, Sig, null) : (null, null, null, Prn);

        public string Json
        {
            get
            {
                var json = new StringBuilder($"{{\"PRN\":{Prn}");
                if (Gnss is { } g)
                {
                    json.Append(CultureInfo.InvariantCulture, $",\"gnssid\":{g}");
                }

                if (Sv is { } s)
                {
                    json.Append(CultureInfo.InvariantCulture, $",\"svid\":{s}");
                }

                if (Sig is { } sig)
                {
                    json.Append(CultureInfo.InvariantCulture, $",\"sigid\":{sig}");
                }

                if (Used is { } u)
                {
                    json.Append(u ? ",\"used\":true" : ",\"used\":false");
                }

                return json.Append(Members(Fields)).Append('}').ToString();
            }
        }
    }

    private static Gen<((SkyField Field, string Name, double? Value)[] Fields, SatelliteSpec[]? Satellites)> Skies =>
        from fields in Present(SkyTable)
        from hasArray in Gen.Frequency((1, Gen.Constant(false)), (4, Gen.Constant(true)))
        from satellites in Gen.Choose(0, 12).SelectMany(n => Gen.ArrayOf(Satellites, n))
        select (fields, hasArray ? satellites : null);

    private static string SkyLine(string device, (SkyField Field, string Name, double? Value)[] fields, SatelliteSpec[]? satellites, int second = 0)
    {
        var array = satellites is null ? string.Empty : $",\"satellites\":[{string.Join(',', satellites.Select(s => s.Json))}]";
        return $$"""{"class":"SKY","device":"{{device}}","time":"2026-11-23T10:00:{{second:00}}.000Z"{{Members(fields)}}{{array}}}""";
    }

    /// <summary>The satellites the state should hold: the last of each identity, in first-seen order.</summary>
    private static List<SatelliteSpec> Expected(SatelliteSpec[] satellites)
    {
        var result = new List<SatelliteSpec>();
        foreach (var satellite in satellites)
        {
            var index = result.FindIndex(s => s.Key == satellite.Key);
            if (index >= 0)
            {
                result[index] = satellite;
            }
            else
            {
                result.Add(satellite);
            }
        }

        return result;
    }

    [Fact]
    public void B_a_generated_sky_produces_exactly_its_fields_and_satellites()
    {
        Check.One(Config, Prop.ForAll(Skies.ToArbitrary(), sky =>
        {
            var (fields, satellites) = sky;
            var line = SkyLine("/dev/ttyACM0", fields, satellites);
            var parsed = GpsdParser.Parse(Encoding.UTF8.GetBytes(line));
            parsed.Error.ShouldBeNull(line);
            parsed.FieldErrors.ShouldAllBe(e => e.Reason == ParseErrorReason.DuplicateSatellite, line);

            var store = new StateStore(TimeProvider.System);
            store.Apply(parsed.Report!).ShouldBeNull();
            var state = store.Current.Devices["/dev/ttyACM0"].Sky!;

            foreach (var (field, name, value) in fields)
            {
                (state[field]?.Value).ShouldBe(value, $"{name} in {line}");
            }

            if (satellites is null)
            {
                state.Satellites.ShouldBeNull();
                return;
            }

            var expected = Expected(satellites);
            var actual = state.Satellites!.Value.Value;
            parsed.FieldErrors.Count.ShouldBe(satellites.Length - expected.Count);
            actual.Length.ShouldBe(expected.Count, line);
            for (var i = 0; i < expected.Count; i++)
            {
                var (want, got) = (expected[i], actual[i]);
                (got.Prn, got.GnssId, got.SvId, got.SigId, got.Used).ShouldBe((want.Prn, want.Gnss, want.Sv, want.Sig, want.Used), line);
                foreach (var (field, name, value) in want.Fields)
                {
                    got.Fields[field].ShouldBe(value, $"satellite {i} {name} in {line}");
                }
            }
        }));
    }

    // ---------------------------------------------------------------------------------------
    // (c) Random sequences: no duplicate identities, nothing for a removed device, nothing for
    // a satellite missing from the latest SKY.

    private abstract record Step(string Device);

    private sealed record TpvStep(string Device, int Mode) : Step(Device);

    private sealed record SkyStep(string Device, SatelliteSpec[]? Satellites) : Step(Device);

    private sealed record DeviceStep(string Device, bool Activated) : Step(Device);

    private static Gen<Step> Steps =>
        Gen.Elements(DevicePaths).SelectMany(device => Gen.Frequency(
            (3, Gen.Choose(0, 3).Select(mode => (Step)new TpvStep(device, mode))),
            (3, Gen.Frequency((1, Gen.Constant(false)), (4, Gen.Constant(true)))
                .SelectMany(hasArray => Gen.Choose(0, 8).SelectMany(n => Gen.ArrayOf(Satellites, n)).Select(sats => hasArray ? sats : null))
                .Select(sats => (Step)new SkyStep(device, sats))),
            (1, Gen.Elements(true, false).Select(on => (Step)new DeviceStep(device, on)))));

    [Fact]
    public void C_random_sequences_keep_state_consistent()
    {
        var sequences = Gen.Choose(1, 40).SelectMany(n => Gen.ArrayOf(Steps, n));
        Check.One(Config, Prop.ForAll(sequences.ToArbitrary(), steps =>
        {
            var store = new StateStore(TimeProvider.System);
            var devices = new HashSet<string>(StringComparer.Ordinal);
            var satellites = new Dictionary<string, List<SatelliteSpec>?>(StringComparer.Ordinal);
            var second = 0;

            foreach (var step in steps)
            {
                second = (second + 1) % 60;
                var line = step switch
                {
                    TpvStep t => $$"""{"class":"TPV","device":"{{t.Device}}","mode":{{t.Mode}},"time":"2026-11-23T10:00:{{second:00}}.000Z","lat":1.5}""",
                    SkyStep s => SkyLine(s.Device, [], s.Satellites, second),
                    DeviceStep { Activated: true } d => $$"""{"class":"DEVICE","path":"{{d.Device}}","activated":"2026-11-23T10:00:00.000Z"}""",
                    DeviceStep d => $$"""{"class":"DEVICE","path":"{{d.Device}}","activated":0}""",
                    _ => throw new InvalidOperationException(),
                };

                var parsed = GpsdParser.Parse(Encoding.UTF8.GetBytes(line));
                parsed.Error.ShouldBeNull(line);
                store.Apply(parsed.Report!).ShouldBeNull(line);

                // The model.
                if (step is DeviceStep { Activated: false })
                {
                    devices.Remove(step.Device);
                    satellites.Remove(step.Device);
                }
                else
                {
                    devices.Add(step.Device);
                    if (step is SkyStep { Satellites: { } array })
                    {
                        satellites[step.Device] = Expected(array);
                    }
                }

                // The invariants, after every step.
                store.Current.Devices.Keys.ShouldBe(devices.Order(StringComparer.Ordinal), line);
                foreach (var (path, device) in store.Current.Devices)
                {
                    var held = device.Sky?.Satellites?.Value ?? [];
                    held.Select(SatelliteId.Of).Distinct().Count().ShouldBe(held.Length, $"duplicate identity on {path}");

                    var expected = satellites.GetValueOrDefault(path) ?? [];
                    held.Select(s => (s.GnssId, s.SvId, s.SigId, s.Prn))
                        .ShouldBe(expected.Select(s => (s.Gnss, s.Sv, s.Sig, s.Prn)), $"satellites on {path}");
                }
            }
        }));
    }
}
