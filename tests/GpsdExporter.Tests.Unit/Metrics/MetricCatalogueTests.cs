using System.Text.RegularExpressions;
using GpsdExporter.Metrics;

namespace GpsdExporter.Tests.Unit.Metrics;

/// <summary>T7.1: the catalogue follows the Prometheus naming rules (AC7.1, AC7.2).</summary>
public sealed partial class MetricCatalogueTests
{
    /// <summary>The name suffix each unit requires (before <c>_total</c> on a counter).</summary>
    private static readonly Dictionary<string, string> UnitSuffixes = new(StringComparer.Ordinal)
    {
        ["s"] = "_seconds",
        ["m"] = "_meters",
        ["m/s"] = "_meters_per_second",
        ["s/s"] = "_seconds_per_second",
        ["deg"] = "_degrees",
        ["bit/s"] = "_bits_per_second",
        ["Cel"] = "_celsius",
        ["dBHz"] = "_dbhz",
        ["log2(s)"] = "_log2_seconds",
    };

    public static TheoryData<string> Names => [.. MetricCatalogue.All.Select(m => m.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void Name_is_snake_case_with_the_gpsd_prefix(string name) =>
        MetricName().IsMatch(name).ShouldBeTrue(name);

    [Theory]
    [MemberData(nameof(Names))]
    public void Help_is_present(string name) =>
        Metric(name).Help.ShouldNotBeNullOrWhiteSpace();

    [Theory]
    [MemberData(nameof(Names))]
    public void Counters_and_only_counters_end_in_total(string name) =>
        name.EndsWith("_total", StringComparison.Ordinal).ShouldBe(Metric(name).Kind == MetricKind.Counter, name);

    [Theory]
    [MemberData(nameof(Names))]
    public void Info_metrics_are_dimensionless_gauges(string name)
    {
        var metric = Metric(name);
        if (name.EndsWith("_info", StringComparison.Ordinal))
        {
            metric.Kind.ShouldBe(MetricKind.Gauge);
            metric.Unit.ShouldBe("1");
            metric.Labels.ShouldNotBeEmpty();
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Unit_matches_the_name_suffix(string name)
    {
        var metric = Metric(name);
        var stem = metric.Kind == MetricKind.Counter ? name[..^"_total".Length] : name;

        if (metric.Unit == "1")
        {
            // A dimensionless metric must not look like it has a unit.
            UnitSuffixes.Values.ShouldNotContain(s => stem.EndsWith(s, StringComparison.Ordinal), name);
        }
        else
        {
            UnitSuffixes.ShouldContainKey(metric.Unit, name);
            var suffix = UnitSuffixes[metric.Unit];

            // The longest matching suffix decides, so _meters_per_second is not read as _second.
            var longest = UnitSuffixes.Values.Where(s => stem.EndsWith(s, StringComparison.Ordinal)).MaxBy(s => s.Length);
            longest.ShouldBe(suffix, name);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Timestamps_are_in_seconds(string name)
    {
        if (name.Contains("timestamp", StringComparison.Ordinal))
        {
            name.ShouldEndWith("_timestamp_seconds");
            Metric(name).Unit.ShouldBe("s");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Labels_are_snake_case_unique_and_not_reserved(string name)
    {
        var labels = Metric(name).Labels;
        labels.Distinct(StringComparer.Ordinal).Count().ShouldBe(labels.Length, name);
        labels.ShouldAllBe(l => LabelName().IsMatch(l));
        labels.ShouldNotContain("le");
        labels.ShouldNotContain("quantile");
    }

    [Fact]
    public void Names_are_unique() =>
        MetricCatalogue.All.Select(m => m.Name).ShouldBeUnique();

    [Fact]
    public void Error_estimates_say_their_confidence_level_is_unknown()
    {
        var estimates = MetricCatalogue.All.Where(m => m.Name.StartsWith("gpsd_tpv_", StringComparison.Ordinal) && m.Name.Contains("_error_", StringComparison.Ordinal)).ToList();

        estimates.Count.ShouldBe(7);
        estimates.ShouldAllBe(m => m.Help.Contains("confidence level", StringComparison.Ordinal));
    }

    [Fact]
    public void Satellite_metrics_carry_the_satellite_labels() =>
        MetricCatalogue.All.Where(m => m.Name.StartsWith("gpsd_satellite_", StringComparison.Ordinal))
            .ShouldAllBe(m => m.Labels.SequenceEqual(MetricCatalogue.SatelliteLabels));

    [Fact]
    public void Every_gpsd_derived_metric_has_a_device_label()
    {
        string[] withoutDevice = ["gpsd_up", "gpsd_version_info"];

        MetricCatalogue.All
            .Where(m => !m.Name.StartsWith("gpsd_exporter_", StringComparison.Ordinal) && !withoutDevice.Contains(m.Name))
            .ShouldAllBe(m => m.Labels.Contains("device"));
    }

    private static MetricDefinition Metric(string name) => MetricCatalogue.All.Single(m => m.Name == name);

    [GeneratedRegex("^gpsd_[a-z0-9_]+$")]
    private static partial Regex MetricName();

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex LabelName();
}
