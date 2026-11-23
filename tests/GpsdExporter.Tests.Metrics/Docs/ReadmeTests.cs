using System.Text;
using System.Text.RegularExpressions;
using GpsdExporter.Metrics;

namespace GpsdExporter.Tests.Metrics.Docs;

/// <summary>
/// T9.2 / AC9.2: the README's metrics reference is the catalogue, rendered. The rendered table
/// is a Verify snapshot, so a catalogue change shows up as a snapshot diff, and the README must
/// hold exactly the same text between its metrics-table markers.
/// </summary>
public sealed partial class ReadmeTests
{
    private const string Begin = "<!-- metrics-table:begin -->";
    private const string End = "<!-- metrics-table:end -->";
    private const string MigrationBegin = "<!-- migration-table:begin -->";
    private const string MigrationEnd = "<!-- migration-table:end -->";

    private static readonly string[] HistogramSuffixes = ["_bucket", "_sum", "_count"];

    private static string Readme => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "README.md")).ReplaceLineEndings("\n");

    [Fact]
    public Task T9_2_the_metrics_table_matches_the_approved_snapshot() =>
        Verify(Render(), extension: "md");

    [Fact]
    public void T9_2_the_README_metrics_table_is_the_catalogue()
    {
        var section = Between(Readme, Begin, End);

        section.ShouldBe(
            Render(),
            "The README's metrics table is out of date. Replace the text between the metrics-table markers with "
            + "the approved snapshot, tests/GpsdExporter.Tests.Metrics/Snapshots/ReadmeTests.T9_2_the_metrics_table_matches_the_approved_snapshot.verified.md.");
    }

    [Fact]
    public void Every_new_name_in_the_migration_table_is_in_the_catalogue()
    {
        var table = Between(Readme, MigrationBegin, MigrationEnd);
        var rows = table.Split('\n').Where(l => l.StartsWith("| `gpsd_", StringComparison.Ordinal)).ToList();
        rows.Count.ShouldBeGreaterThan(80, "the migration table should list every Go exporter metric");

        foreach (var row in rows)
        {
            var cells = row.Split('|', StringSplitOptions.TrimEntries);
            var replacement = cells[2];
            foreach (var name in SeriesName().Matches(replacement).Select(m => m.Value))
            {
                Resolves(name).ShouldBeTrue($"{name} in the migration row {row} is not in the catalogue.");
            }

            (SeriesName().IsMatch(replacement) || replacement.StartsWith("Removed", StringComparison.Ordinal))
                .ShouldBeTrue($"The migration row {row} names neither a replacement nor Removed.");
        }

        // No Go name is listed twice.
        rows.Select(r => r.Split('|', StringSplitOptions.TrimEntries)[1]).ShouldBeUnique();
    }

    /// <summary>The metrics reference as Markdown, one table per group, in catalogue order.</summary>
    internal static string Render()
    {
        var sb = new StringBuilder();
        foreach (var group in MetricCatalogue.All.GroupBy(Group))
        {
            sb.Append("### ").Append(group.Key).Append("\n\n");
            sb.Append("| Name | Type | Unit | Labels | Help |\n");
            sb.Append("| -- | -- | -- | -- | -- |\n");
            foreach (var metric in group)
            {
                sb.Append("| `").Append(metric.Name).Append("` | ")
                  .Append(metric.Kind.ToString().ToLowerInvariant()).Append(" | ")
                  .Append(Escape(metric.Unit)).Append(" | ")
                  .Append(string.Join(", ", metric.Labels.Select(l => $"`{l}`"))).Append(" | ")
                  .Append(Escape(metric.Help)).Append(" |\n");
            }

            sb.Append('\n');
        }

        return sb.ToString().TrimEnd('\n') + "\n";
    }

    private static string Group(MetricDefinition metric) => metric.Name switch
    {
        _ when metric.Name.StartsWith("gpsd_exporter_", StringComparison.Ordinal) => "Exporter",
        _ when metric.Name.StartsWith("gpsd_tpv_", StringComparison.Ordinal) => "Position, velocity and time (TPV)",
        _ when metric.Name.StartsWith("gpsd_sky_", StringComparison.Ordinal)
            || metric.Name.StartsWith("gpsd_satellite_", StringComparison.Ordinal) => "Sky view and satellites (SKY)",
        _ when metric.Name.StartsWith("gpsd_pps_", StringComparison.Ordinal)
            || metric.Name.StartsWith("gpsd_toff_", StringComparison.Ordinal)
            || metric.Name.StartsWith("gpsd_osc_", StringComparison.Ordinal) => "Timing (PPS, TOFF and OSC)",
        _ => "Connection and devices",
    };

    private static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Between(string text, string begin, string end)
    {
        var start = text.IndexOf(begin, StringComparison.Ordinal);
        var stop = text.IndexOf(end, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"README.md has no {begin} marker.");
        stop.ShouldBeGreaterThan(start, $"README.md has no {end} marker after {begin}.");
        return text[(start + begin.Length)..stop].Trim('\n') + "\n";
    }

    private static bool Resolves(string name) =>
        MetricCatalogue.All.Any(m => m.Name == name)
        || HistogramSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal)
            && MetricCatalogue.All.Any(m => m.Kind == MetricKind.Histogram && m.Name == name[..^s.Length]));

    [GeneratedRegex(@"\bgpsd_[a-z0-9_]+\b")]
    private static partial Regex SeriesName();
}
