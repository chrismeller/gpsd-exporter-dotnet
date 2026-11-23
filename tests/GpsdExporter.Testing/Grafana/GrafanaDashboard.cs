using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GpsdExporter.Testing.Grafana;

/// <summary>One PromQL query in the dashboard: a panel target or a template variable's query.</summary>
public sealed record DashboardQuery(string Panel, string RefId, string Expr, bool Instant)
{
    public override string ToString() => $"{Panel} [{RefId}]: {Expr}";
}

/// <summary>
/// The repository's <c>grafana-dashboard.json</c>, which the test projects copy next to the
/// test assembly, read as JSON for tests.
/// </summary>
public sealed partial class GrafanaDashboard
{
    private GrafanaDashboard(JsonObject json)
    {
        Json = json;
        var panels = (json["panels"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        Rows = [.. panels.Where(p => (string?)p["type"] == "row")];

        // Rows can hold their panels when collapsed; include those too.
        Panels = [.. panels.Where(p => (string?)p["type"] != "row")
            .Concat(Rows.SelectMany(r => (r["panels"]?.AsArray() ?? []).OfType<JsonObject>()))];

        Variables = [.. (json["templating"]?["list"]?.AsArray() ?? []).OfType<JsonObject>()];

        Queries =
        [
            .. Panels.SelectMany(p => (p["targets"]?.AsArray() ?? []).OfType<JsonObject>().Select(t => new DashboardQuery(
                (string?)p["title"] ?? "(untitled)",
                (string?)t["refId"] ?? "?",
                (string?)t["expr"] ?? string.Empty,
                (bool?)t["instant"] == true && (bool?)t["range"] != true))),
            .. Variables.Where(v => (string?)v["type"] == "query").Select(v => new DashboardQuery(
                $"${(string?)v["name"]}",
                "variable",
                (string?)v["query"]?["query"] ?? (string?)v["query"] ?? string.Empty,
                Instant: true)),
        ];
    }

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "grafana-dashboard.json");

    public JsonObject Json { get; }

    public IReadOnlyList<JsonObject> Rows { get; }

    /// <summary>Every panel that is not a row.</summary>
    public IReadOnlyList<JsonObject> Panels { get; }

    public IReadOnlyList<JsonObject> Variables { get; }

    public IReadOnlyList<DashboardQuery> Queries { get; }

    public static GrafanaDashboard Load() =>
        new(JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject() ?? throw new InvalidDataException("The dashboard is not a JSON object."));

    /// <summary>The <c>gpsd_*</c> series names in a PromQL expression.</summary>
    public static IReadOnlyList<string> SeriesNames(string expr) =>
        [.. SeriesName().Matches(expr).Select(m => m.Value).Distinct(StringComparer.Ordinal)];

    /// <summary>Each <c>gpsd_*{...}</c> selector's series name and the label names it matches on.</summary>
    public static IEnumerable<(string Name, IReadOnlyList<string> Labels)> Selectors(string expr) =>
        Selector().Matches(expr).Select(m => (
            m.Groups["name"].Value,
            (IReadOnlyList<string>)[.. Matcher().Matches(m.Groups["matchers"].Value).Select(l => l.Groups["label"].Value)]));

    /// <summary>The label names in every <c>by (...)</c> and <c>without (...)</c> clause.</summary>
    public static IEnumerable<string> GroupingLabels(string expr) =>
        Grouping().Matches(expr).SelectMany(m => m.Groups["labels"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// The expression as Grafana would send it with every device selected and a 1-minute rate
    /// interval, for querying Prometheus directly.
    /// </summary>
    public static string Interpolate(string expr) =>
        expr.Replace("$device", ".*", StringComparison.Ordinal)
            .Replace("$__rate_interval", "1m", StringComparison.Ordinal);

    [GeneratedRegex(@"\bgpsd_[a-z0-9_]+\b")]
    private static partial Regex SeriesName();

    [GeneratedRegex(@"\b(?<name>gpsd_[a-z0-9_]+)\{(?<matchers>[^}]*)\}")]
    private static partial Regex Selector();

    [GeneratedRegex(@"(?<label>[a-zA-Z_][a-zA-Z0-9_]*)\s*(=~|!~|!=|=)")]
    private static partial Regex Matcher();

    [GeneratedRegex(@"\b(by|without)\s*\((?<labels>[^)]*)\)")]
    private static partial Regex Grouping();
}
