using GpsdExporter.Metrics;
using GpsdExporter.Testing.Grafana;

namespace GpsdExporter.Tests.Unit.Metrics;

/// <summary>T9.1 / AC9.1: <c>grafana-dashboard.json</c> uses only the catalogue's metrics.</summary>
public sealed class DashboardTests
{
    private static readonly string[] HistogramSuffixes = ["_bucket", "_sum", "_count"];

    private static readonly GrafanaDashboard Dashboard = GrafanaDashboard.Load();

    public static TheoryData<string> Queries => [.. Dashboard.Queries.Select(q => q.ToString())];

    [Fact]
    public void The_dashboard_has_queries() =>
        Dashboard.Queries.Count.ShouldBeGreaterThan(20);

    [Theory]
    [MemberData(nameof(Queries))]
    public void T9_1_every_series_name_is_in_the_catalogue(string query)
    {
        var expr = Find(query).Expr;
        var names = GrafanaDashboard.SeriesNames(expr);

        names.ShouldNotBeEmpty($"{query} uses no gpsd_ series.");
        foreach (var name in names)
        {
            Resolve(name).ShouldNotBeNull($"{name} in {query} is not in the catalogue.");
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void Every_label_matched_or_grouped_on_belongs_to_the_series(string query)
    {
        var expr = Find(query).Expr;
        var metrics = GrafanaDashboard.SeriesNames(expr).Select(Resolve).OfType<MetricDefinition>().ToList();

        foreach (var (name, labels) in GrafanaDashboard.Selectors(expr))
        {
            var metric = Resolve(name).ShouldNotBeNull();
            labels.ShouldBeSubsetOf(metric.Labels, $"{name} in {query}");
        }

        // A by (...) clause may group on any label of the series in the expression, and on le
        // for a histogram's buckets.
        var allowed = metrics.SelectMany(m => m.Labels).Append("le").ToHashSet(StringComparer.Ordinal);
        GrafanaDashboard.GroupingLabels(expr).ShouldBeSubsetOf(allowed, query);
    }

    [Fact]
    public void AC9_1_has_a_row_for_each_required_section()
    {
        var rows = Dashboard.Rows.Select(r => (string?)r["title"]).ToList();

        rows.ShouldBe(["Fix", "Position", "Satellites", "Dilution of precision", "Timing (PPS and TOFF)", "Exporter health"]);
    }

    [Theory]
    [InlineData("gpsd_tpv_mode")]
    [InlineData("gpsd_tpv_status")]
    [InlineData("gpsd_tpv_latitude_degrees")]
    [InlineData("gpsd_tpv_longitude_degrees")]
    [InlineData("gpsd_satellite_used")]
    [InlineData("gpsd_satellite_snr_dbhz")]
    [InlineData("gpsd_sky_dop")]
    [InlineData("gpsd_pps_offset_seconds")]
    [InlineData("gpsd_toff_offset_seconds")]
    [InlineData("gpsd_up")]
    [InlineData("gpsd_exporter_connection_attempts_total")]
    [InlineData("gpsd_exporter_parse_errors_total")]
    public void AC9_1_charts_the_required_metric(string name) =>
        Dashboard.Queries.ShouldContain(q => GrafanaDashboard.SeriesNames(q.Expr).Contains(name));

    [Fact]
    public void Satellites_are_broken_down_by_constellation_and_signal() =>
        Dashboard.Queries.ShouldContain(q =>
            q.Expr.Contains("gpsd_satellite_snr_dbhz", StringComparison.Ordinal)
            && q.Expr.Contains("constellation", StringComparison.Ordinal)
            && q.Expr.Contains("sigid", StringComparison.Ordinal));

    [Fact]
    public void Device_is_a_multi_value_variable_that_every_device_query_uses()
    {
        var device = Dashboard.Variables.Where(v => (string?)v["name"] == "device").ShouldHaveSingleItem();
        ((bool?)device["multi"]).ShouldBe(true);
        ((bool?)device["includeAll"]).ShouldBe(true);

        foreach (var query in Dashboard.Queries.Where(q => q.RefId != "variable"))
        {
            var metrics = GrafanaDashboard.SeriesNames(query.Expr).Select(Resolve).OfType<MetricDefinition>();
            if (metrics.Any(m => m.Labels.Contains("device")))
            {
                query.Expr.ShouldContain("device=~\"$device\"", customMessage: query.ToString());
            }
        }
    }

    [Fact]
    public void Every_panel_uses_the_datasource_variable()
    {
        Dashboard.Variables.ShouldContain(v => (string?)v["name"] == "datasource" && (string?)v["type"] == "datasource");
        foreach (var panel in Dashboard.Panels)
        {
            ((string?)panel["datasource"]?["uid"]).ShouldBe("${datasource}", (string?)panel["title"]);
        }
    }

    [Fact]
    public void Panel_ids_are_unique() =>
        Dashboard.Panels.Concat(Dashboard.Rows).Select(p => (int?)p["id"]).ShouldBeUnique();

    private static DashboardQuery Find(string query) => Dashboard.Queries.Single(q => q.ToString() == query);

    private static MetricDefinition? Resolve(string name)
    {
        var exact = MetricCatalogue.All.FirstOrDefault(m => m.Name == name);
        if (exact is not null)
        {
            return exact;
        }

        var suffix = HistogramSuffixes.FirstOrDefault(s => name.EndsWith(s, StringComparison.Ordinal));
        return suffix is null
            ? null
            : MetricCatalogue.All.FirstOrDefault(m => m.Kind == MetricKind.Histogram && m.Name == name[..^suffix.Length]);
    }
}
