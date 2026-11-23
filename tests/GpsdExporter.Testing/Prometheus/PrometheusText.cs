using System.Globalization;
using System.Text;

namespace GpsdExporter.Testing.Prometheus;

/// <summary>One sample line: series name, labels, value and optional timestamp.</summary>
public sealed record PrometheusSample(string Name, IReadOnlyDictionary<string, string> Labels, double Value, long? TimestampMs)
{
    /// <summary>The series in Prometheus notation, labels sorted: <c>name{a="1",b="2"}</c>.</summary>
    public string Series => PrometheusText.FormatSeries(Name, Labels);

    public override string ToString() => $"{Series} {PrometheusText.FormatValue(Value)}";
}

/// <summary>A metric family: the <c># HELP</c> and <c># TYPE</c> metadata and its samples.</summary>
public sealed record PrometheusFamily(string Name, string? Type, string? Help, IReadOnlyList<PrometheusSample> Samples);

/// <summary>
/// A parser for the Prometheus text exposition format (0.0.4, and the OpenMetrics subset the
/// OpenTelemetry exporter produces), for asserting on <c>/metrics</c> in tests.
/// </summary>
/// <remarks>
/// Samples are grouped into the family declared by the closest preceding <c># TYPE</c> or
/// <c># HELP</c> whose name matches, directly or with a <c>_total</c>, <c>_bucket</c>,
/// <c>_sum</c>, <c>_count</c>, <c>_created</c> or <c>_info</c> suffix. A sample with no
/// declared family gets a family of its own with no type or help.
/// </remarks>
public sealed class PrometheusText
{
    private static readonly string[] FamilySuffixes = ["_total", "_bucket", "_sum", "_count", "_created", "_info"];

    private PrometheusText(IReadOnlyList<PrometheusFamily> families)
    {
        Families = families;
        Samples = [.. families.SelectMany(f => f.Samples)];
    }

    public IReadOnlyList<PrometheusFamily> Families { get; }

    public IReadOnlyList<PrometheusSample> Samples { get; }

    public static PrometheusText Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var order = new List<string>();
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var helps = new Dictionary<string, string>(StringComparer.Ordinal);
        var samples = new Dictionary<string, List<PrometheusSample>>(StringComparer.Ordinal);

        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                if (line[0] == '#')
                {
                    ParseComment(line, order, types, helps);
                    continue;
                }

                var sample = ParseSample(line);
                var family = FamilyFor(sample.Name, order);
                if (family is null)
                {
                    family = sample.Name;
                    order.Add(family);
                }

                if (!samples.TryGetValue(family, out var list))
                {
                    samples[family] = list = [];
                }

                list.Add(sample);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"Line {lineNumber}: {ex.Message}: {line}", ex);
            }
        }

        return new PrometheusText([.. order.Select(name => new PrometheusFamily(
            name,
            types.GetValueOrDefault(name),
            helps.GetValueOrDefault(name),
            samples.TryGetValue(name, out var s) ? s : []))]);
    }

    /// <summary>The family with this name, or null.</summary>
    public PrometheusFamily? Family(string name) => Families.FirstOrDefault(f => f.Name == name);

    /// <summary>Every sample with this exact series name, in exposition order.</summary>
    public IReadOnlyList<PrometheusSample> All(string name) => [.. Samples.Where(s => s.Name == name)];

    /// <summary>
    /// The sample with this name and exactly this label set (order does not matter), or null.
    /// Throws if the scrape has duplicate series, which Prometheus would reject.
    /// </summary>
    public PrometheusSample? Find(string name, params (string Name, string Value)[] labels)
    {
        var wanted = labels.ToDictionary(l => l.Name, l => l.Value, StringComparer.Ordinal);
        var matches = Samples.Where(s => s.Name == name && SameLabels(s.Labels, wanted)).ToList();
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException($"Duplicate series {FormatSeries(name, wanted)} in the scrape."),
        };
    }

    public bool Has(string name, params (string Name, string Value)[] labels) => Find(name, labels) is not null;

    /// <summary>The value of the series, or an exception that lists the series with this name that do exist.</summary>
    public double Value(string name, params (string Name, string Value)[] labels)
    {
        var sample = Find(name, labels);
        if (sample is not null)
        {
            return sample.Value;
        }

        var wanted = FormatSeries(name, labels.ToDictionary(l => l.Name, l => l.Value, StringComparer.Ordinal));
        var existing = All(name);
        var detail = existing.Count == 0
            ? $"no series named {name} at all"
            : "series with that name: " + string.Join(", ", existing.Select(s => s.Series));
        throw new KeyNotFoundException($"{wanted} is not in the scrape; {detail}.");
    }

    /// <summary>Series names in the scrape that start with <paramref name="prefix"/>, distinct and sorted.</summary>
    public IReadOnlyList<string> NamesStartingWith(string prefix) =>
        [.. Samples.Select(s => s.Name).Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>Series that appear more than once with the same label set.</summary>
    public IReadOnlyList<string> DuplicateSeries() =>
        [.. Samples.GroupBy(s => s.Series, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key)];

    internal static string FormatSeries(string name, IReadOnlyDictionary<string, string> labels)
    {
        if (labels.Count == 0)
        {
            return name;
        }

        var sb = new StringBuilder(name).Append('{');
        var first = true;
        foreach (var (key, value) in labels.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append(key).Append("=\"").Append(EscapeLabelValue(value)).Append('"');
        }

        return sb.Append('}').ToString();
    }

    internal static string FormatValue(double value) => value switch
    {
        double.PositiveInfinity => "+Inf",
        double.NegativeInfinity => "-Inf",
        _ when double.IsNaN(value) => "NaN",
        _ => value.ToString("R", CultureInfo.InvariantCulture),
    };

    internal static string EscapeLabelValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal)
             .Replace("\n", "\\n", StringComparison.Ordinal);

    private static bool SameLabels(IReadOnlyDictionary<string, string> actual, Dictionary<string, string> wanted) =>
        actual.Count == wanted.Count && wanted.All(w => actual.TryGetValue(w.Key, out var v) && v == w.Value);

    private static string? FamilyFor(string sampleName, List<string> declared)
    {
        // The most recent declaration wins, so a later family with the same prefix is preferred.
        for (var i = declared.Count - 1; i >= 0; i--)
        {
            var family = declared[i];
            if (sampleName == family)
            {
                return family;
            }

            if (sampleName.StartsWith(family, StringComparison.Ordinal)
                && FamilySuffixes.Contains(sampleName[family.Length..], StringComparer.Ordinal))
            {
                return family;
            }
        }

        return null;
    }

    private static void ParseComment(string line, List<string> order, Dictionary<string, string> types, Dictionary<string, string> helps)
    {
        // "# HELP name text" and "# TYPE name type"; any other comment (including "# EOF") is ignored.
        var parts = line.Split(' ', 4);
        if (parts.Length < 3 || parts[0] != "#" || parts[1] is not ("HELP" or "TYPE"))
        {
            return;
        }

        var name = parts[2];
        var rest = parts.Length == 4 ? parts[3] : string.Empty;
        if (!order.Contains(name))
        {
            order.Add(name);
        }

        if (parts[1] == "TYPE")
        {
            types[name] = rest;
        }
        else
        {
            helps[name] = UnescapeHelp(rest);
        }
    }

    private static PrometheusSample ParseSample(string line)
    {
        var i = 0;
        while (i < line.Length && line[i] is not ('{' or ' ' or '\t'))
        {
            i++;
        }

        var name = line[..i];
        if (name.Length == 0)
        {
            throw new FormatException("missing series name");
        }

        var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (i < line.Length && line[i] == '{')
        {
            i = ParseLabels(line, i + 1, labels);
        }

        var tail = line[i..].Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tail.Length is 0 or > 2)
        {
            throw new FormatException("expected a value and an optional timestamp");
        }

        long? timestamp = tail.Length == 2
            ? long.Parse(tail[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
            : null;

        return new PrometheusSample(name, labels, ParseValue(tail[0]), timestamp);
    }

    private static int ParseLabels(string line, int i, SortedDictionary<string, string> labels)
    {
        while (true)
        {
            while (i < line.Length && line[i] is ' ' or ',')
            {
                i++;
            }

            if (i >= line.Length)
            {
                throw new FormatException("unterminated label set");
            }

            if (line[i] == '}')
            {
                return i + 1;
            }

            var eq = line.IndexOf('=', i);
            if (eq < 0 || eq + 1 >= line.Length || line[eq + 1] != '"')
            {
                throw new FormatException("expected label=\"value\"");
            }

            var key = line[i..eq].Trim();
            var value = new StringBuilder();
            i = eq + 2;
            while (true)
            {
                if (i >= line.Length)
                {
                    throw new FormatException($"unterminated value for label {key}");
                }

                var c = line[i++];
                if (c == '"')
                {
                    break;
                }

                if (c == '\\')
                {
                    if (i >= line.Length)
                    {
                        throw new FormatException("dangling escape");
                    }

                    var e = line[i++];
                    value.Append(e switch
                    {
                        'n' => '\n',
                        '\\' => '\\',
                        '"' => '"',
                        _ => throw new FormatException($"invalid escape \\{e} in label {key}"),
                    });
                }
                else
                {
                    value.Append(c);
                }
            }

            if (!labels.TryAdd(key, value.ToString()))
            {
                throw new FormatException($"label {key} appears twice");
            }
        }
    }

    private static double ParseValue(string text) => text switch
    {
        "+Inf" or "Inf" => double.PositiveInfinity,
        "-Inf" => double.NegativeInfinity,
        "NaN" => double.NaN,
        _ => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    private static string UnescapeHelp(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is 'n' or '\\')
            {
                sb.Append(text[++i] == 'n' ? '\n' : '\\');
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        return sb.ToString();
    }
}
