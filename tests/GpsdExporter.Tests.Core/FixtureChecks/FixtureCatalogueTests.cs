using System.Text;
using System.Text.Json;
using GpsdExporter.Testing;

namespace GpsdExporter.Tests.Core.FixtureChecks;

/// <summary>
/// Checks the fixtures themselves: every catalogue file exists and loads, and the
/// synthetic sessions use the field names, JSON types and value ranges of gpsd_json(5).
/// </summary>
public sealed class FixtureCatalogueTests
{
    public static TheoryData<string> CatalogueFixtures() => [.. Fixtures.Catalogue];

    public static TheoryData<string> AllFixtures() => [.. Fixtures.All()];

    public static TheoryData<string> WellFormedFixtures() =>
        [.. Fixtures.All().Where(f => f is not ("bad-input.jsonl" or "doc-examples.jsonl"))];

    public static TheoryData<string> SyntheticSessions() =>
        [.. Fixtures.Catalogue.Where(f => f.StartsWith("session-", StringComparison.Ordinal))];

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Fixture_exists_loads_and_has_a_header_comment(string name)
    {
        File.ReadLines(Fixtures.PathOf(name)).First().ShouldStartWith("#");

        var lines = Fixtures.LoadBytes(name);

        lines.ShouldNotBeEmpty();
        lines.ShouldAllBe(l => l.Length > 0 && l[0] != (byte)'#');
    }

    [Fact]
    public void Catalogue_matches_the_files_on_disk()
    {
        var onDisk = Directory.EnumerateFiles(Fixtures.DirectoryPath, "*.jsonl")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        onDisk.ShouldBe(Fixtures.Catalogue.Order(StringComparer.Ordinal));
        Fixtures.GpsdRegress().Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Gpsd_licence_is_next_to_the_gpsd_derived_fixtures()
    {
        var licence = File.ReadAllText(Path.Combine(Fixtures.DirectoryPath, "COPYING.gpsd"));

        licence.ShouldContain("BSD-2-Clause");
    }

    [Fact]
    public void Doc_examples_keep_the_man_pages_two_wrong_examples_verbatim()
    {
        var lines = Fixtures.Load("doc-examples.jsonl");

        lines.ShouldContain(l => l.StartsWith("{\"class\":\"VERSION\",\"version\":\"2.40dev\"", StringComparison.Ordinal));
        lines.ShouldContain(l => l.StartsWith("{\"class\"=\"DEVICES\"", StringComparison.Ordinal));
        lines.ShouldContain("{\"class\":\"DEVICE\",\"path\":\"/dev/pts1\",\"activated\":0}");
        foreach (var cls in new[] { "TPV", "SKY", "GST", "ATT", "TOFF", "PPS", "OSC", "VERSION", "WATCH", "POLL", "DEVICE", "ACK", "ERROR" })
        {
            lines.ShouldContain(l => l.Contains($"\"class\":\"{cls}\"", StringComparison.Ordinal), $"no {cls} example");
        }
    }

    [Fact]
    public void Bad_input_has_an_over_long_line_and_invalid_utf8()
    {
        var lines = Fixtures.LoadBytes("bad-input.jsonl");

        lines.ShouldContain(l => l.Length > 64 * 1024);
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        lines.ShouldContain(l => !IsValidUtf8(strict, l));
    }

    [Theory]
    [MemberData(nameof(WellFormedFixtures))]
    public void Every_line_is_a_json_object_with_a_class(string name)
    {
        foreach (var line in Fixtures.LoadBytes(name))
        {
            using var doc = JsonDocument.Parse(line);
            doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
            doc.RootElement.GetProperty("class").ValueKind.ShouldBe(JsonValueKind.String);
        }
    }

    [Theory]
    [MemberData(nameof(SyntheticSessions))]
    public void Synthetic_sessions_follow_the_man_page_tables(string name)
    {
        var problems = new List<string>();
        foreach (var line in Fixtures.Load(name))
        {
            using var doc = JsonDocument.Parse(line);
            GpsdSchema.Check(doc.RootElement, problems);
        }

        problems.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(SyntheticSessions))]
    public void Synthetic_sessions_open_with_version_devices_and_watch(string name)
    {
        var classes = Fixtures.Load(name).Take(3).Select(l =>
        {
            using var doc = JsonDocument.Parse(l);
            return doc.RootElement.GetProperty("class").GetString();
        });

        classes.ShouldBe(["VERSION", "DEVICES", "WATCH"]);
    }

    private static bool IsValidUtf8(UTF8Encoding strict, byte[] bytes)
    {
        try
        {
            _ = strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
