using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Core.Prometheus;

public sealed class PrometheusTextTests
{
    private static readonly PrometheusText Scrape = PrometheusText.Parse(SampleExposition.Text);

    [Fact]
    public void Reads_help_type_and_samples_per_family()
    {
        var up = Scrape.Family("gpsd_up").ShouldNotBeNull();

        up.Type.ShouldBe("gauge");
        up.Help.ShouldBe("Whether the exporter is connected to gpsd and has received VERSION.");
        up.Samples.ShouldHaveSingleItem().Value.ShouldBe(1);
    }

    [Fact]
    public void Reads_counters_and_their_labels()
    {
        Scrape.Family("gpsd_exporter_messages_total").ShouldNotBeNull().Type.ShouldBe("counter");
        Scrape.Value("gpsd_exporter_messages_total", ("class", "TPV")).ShouldBe(42);
        Scrape.Value("gpsd_exporter_messages_total", ("class", "SKY")).ShouldBe(7);
    }

    [Fact]
    public void Groups_counter_samples_under_a_family_declared_without_the_total_suffix()
    {
        var scrape = PrometheusText.Parse("""
            # TYPE requests counter
            requests_total 3
            requests_created 1700000000
            """);

        scrape.Families.ShouldHaveSingleItem().Samples.Select(s => s.Name).ShouldBe(["requests_total", "requests_created"]);
    }

    [Fact]
    public void Unescapes_label_values()
    {
        var info = Scrape.All("gpsd_device_info").ShouldHaveSingleItem();

        info.Labels["subtype"].ShouldBe("SW \"EXT\" CORE 1.00\\HW\nline2");
        info.Labels["driver"].ShouldBe("u-blox");
    }

    [Fact]
    public void Reads_histograms_into_one_family()
    {
        var histogram = Scrape.Family("gpsd_exporter_scrape_duration_seconds").ShouldNotBeNull();

        histogram.Type.ShouldBe("histogram");
        histogram.Samples.Count.ShouldBe(5);
        Scrape.Value("gpsd_exporter_scrape_duration_seconds_bucket", ("le", "+Inf")).ShouldBe(4);
        Scrape.Value("gpsd_exporter_scrape_duration_seconds_bucket", ("le", "0.005")).ShouldBe(3);
        Scrape.Value("gpsd_exporter_scrape_duration_seconds_sum").ShouldBe(0.0123);
        Scrape.Value("gpsd_exporter_scrape_duration_seconds_count").ShouldBe(4);
    }

    [Fact]
    public void Matches_the_exact_label_set_in_any_order()
    {
        Scrape.Has("gpsd_device_last_report_timestamp_seconds", ("device", "/dev/ttyACM0"), ("class", "TPV")).ShouldBeTrue();
        Scrape.Has("gpsd_device_last_report_timestamp_seconds", ("device", "/dev/ttyACM0")).ShouldBeFalse();
        Scrape.Has("gpsd_tpv_latitude_degrees").ShouldBeFalse();
        Scrape.Has("gpsd_tpv_longitude_degrees", ("device", "/dev/ttyACM0")).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_series_names_the_series_that_do_exist()
    {
        var ex = Should.Throw<KeyNotFoundException>(() => Scrape.Value("gpsd_exporter_messages_total", ("class", "PPS")));

        ex.Message.ShouldContain("gpsd_exporter_messages_total{class=\"PPS\"} is not in the scrape");
        ex.Message.ShouldContain("gpsd_exporter_messages_total{class=\"TPV\"}");
    }

    [Fact]
    public void Lists_series_names_by_prefix()
    {
        Scrape.NamesStartingWith("gpsd_tpv_").ShouldBe(["gpsd_tpv_latitude_degrees"]);
    }

    [Fact]
    public void Reads_special_values_and_timestamps()
    {
        var scrape = PrometheusText.Parse("""
            a +Inf
            b -Inf
            c NaN
            d 1.5e-9 1700000000123
            """);

        scrape.Value("a").ShouldBe(double.PositiveInfinity);
        scrape.Value("b").ShouldBe(double.NegativeInfinity);
        double.IsNaN(scrape.Value("c")).ShouldBeTrue();
        var d = scrape.Find("d").ShouldNotBeNull();
        d.Value.ShouldBe(1.5e-9);
        d.TimestampMs.ShouldBe(1700000000123);
    }

    [Fact]
    public void Detects_duplicate_series()
    {
        var scrape = PrometheusText.Parse("""
            # TYPE x gauge
            x{a="1"} 1
            x{a="1"} 2
            x{a="2"} 3
            """);

        scrape.DuplicateSeries().ShouldBe(["x{a=\"1\"}"]);
        Should.Throw<InvalidOperationException>(() => scrape.Find("x", ("a", "1")));
    }

    [Theory]
    [InlineData("x{a=\"1} 1")]
    [InlineData("x{a=\"\\q\"} 1")]
    [InlineData("x{a=1} 1")]
    [InlineData("x{a=\"1\",a=\"2\"} 1")]
    [InlineData("x")]
    [InlineData("x one")]
    public void Rejects_malformed_lines(string line)
    {
        Should.Throw<FormatException>(() => PrometheusText.Parse(line));
    }
}
