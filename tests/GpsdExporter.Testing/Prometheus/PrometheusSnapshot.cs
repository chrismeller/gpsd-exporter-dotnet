using System.Text;

namespace GpsdExporter.Testing.Prometheus;

/// <summary>
/// Turns a <c>/metrics</c> scrape into stable text for a Verify snapshot (T7.2).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Families are sorted by name and samples within a family by series, so the snapshot
/// does not depend on the order the exporter happens to write them in.</item>
/// <item>Values that change from run to run are replaced: every <c>*_timestamp_seconds</c>
/// series becomes <c>{timestamp}</c>, and every <c>*_duration_seconds</c> histogram sample
/// (<c>_bucket</c>, <c>_sum</c>, <c>_count</c>) becomes <c>{duration}</c>.</item>
/// <item>The <c>version</c>, <c>revision</c> and <c>runtime</c> labels of <c>*_build_info</c>
/// become <c>{scrubbed}</c>.</item>
/// <item>Series timestamps, which the exporter should not emit, are kept so that a snapshot
/// would show them.</item>
/// </list>
/// Names, labels, <c># TYPE</c> and <c># HELP</c> are kept exactly, so a renamed series or a
/// changed label fails the snapshot.
/// </remarks>
public static class PrometheusSnapshot
{
    public const string TimestampPlaceholder = "{timestamp}";
    public const string DurationPlaceholder = "{duration}";
    public const string ScrubbedPlaceholder = "{scrubbed}";

    private static readonly string[] BuildLabels = ["version", "revision", "runtime"];

    public static string Normalize(string exposition) => Normalize(PrometheusText.Parse(exposition));

    public static string Normalize(PrometheusText scrape)
    {
        ArgumentNullException.ThrowIfNull(scrape);
        var sb = new StringBuilder();
        foreach (var family in scrape.Families.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (family.Help is not null)
            {
                sb.Append("# HELP ").Append(family.Name).Append(' ').Append(family.Help.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)).Append('\n');
            }

            if (family.Type is not null)
            {
                sb.Append("# TYPE ").Append(family.Name).Append(' ').Append(family.Type).Append('\n');
            }

            var lines = family.Samples.Select(Format).ToList();
            // Histogram buckets sort by their numeric "le" bound, not as text.
            foreach (var line in lines.OrderBy(l => l.SortKey, StringComparer.Ordinal).ThenBy(l => l.Le))
            {
                sb.Append(line.Text).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static (string SortKey, double Le, string Text) Format(PrometheusSample sample)
    {
        var labels = new Dictionary<string, string>(sample.Labels, StringComparer.Ordinal);
        if (sample.Name.EndsWith("_build_info", StringComparison.Ordinal))
        {
            foreach (var key in BuildLabels.Where(labels.ContainsKey))
            {
                labels[key] = ScrubbedPlaceholder;
            }
        }

        string value;
        if (sample.Name.EndsWith("_timestamp_seconds", StringComparison.Ordinal))
        {
            value = TimestampPlaceholder;
        }
        else if (IsDurationHistogramSample(sample.Name))
        {
            value = DurationPlaceholder;
        }
        else
        {
            value = PrometheusText.FormatValue(sample.Value);
        }

        var le = labels.TryGetValue("le", out var bound)
            ? (bound == "+Inf" ? double.PositiveInfinity : double.Parse(bound, System.Globalization.CultureInfo.InvariantCulture))
            : 0;

        var withoutLe = new Dictionary<string, string>(labels, StringComparer.Ordinal);
        withoutLe.Remove("le");
        var sortKey = PrometheusText.FormatSeries(sample.Name, withoutLe);

        var text = PrometheusText.FormatSeries(sample.Name, labels) + " " + value
            + (sample.TimestampMs is { } ts ? " " + ts.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        return (sortKey, le, text);
    }

    private static bool IsDurationHistogramSample(string name) =>
        name.EndsWith("_duration_seconds_bucket", StringComparison.Ordinal)
        || name.EndsWith("_duration_seconds_sum", StringComparison.Ordinal)
        || name.EndsWith("_duration_seconds_count", StringComparison.Ordinal);
}
