using GpsdExporter.Testing.Prometheus;
using VerifyTests;

namespace GpsdExporter.Tests.Metrics.Prometheus;

public sealed class PrometheusSnapshotTests
{
    [Fact]
    public Task Sample_scrape_matches_the_approved_snapshot() =>
        Verify(PrometheusSnapshot.Normalize(SampleExposition.Text));

    [Fact]
    public void Normalizing_scrubs_timestamps_durations_and_build_labels()
    {
        var normalized = PrometheusSnapshot.Normalize(SampleExposition.Text);

        normalized.ShouldContain("gpsd_device_last_report_timestamp_seconds{class=\"TPV\",device=\"/dev/ttyACM0\"} {timestamp}");
        normalized.ShouldContain("gpsd_exporter_scrape_duration_seconds_sum {duration}");
        normalized.ShouldContain("gpsd_exporter_build_info{revision=\"{scrubbed}\",runtime=\"{scrubbed}\",version=\"{scrubbed}\"} 1");
        normalized.ShouldNotContain("1795428000");
        normalized.ShouldNotContain("1.2.3");
    }

    [Fact]
    public void Normalizing_ignores_the_order_of_families_and_series()
    {
        var reordered = """
            # TYPE b gauge
            b{x="2"} 2
            b{x="1"} 1
            # TYPE a gauge
            a 0
            """;
        var ordered = """
            # TYPE a gauge
            a 0
            # TYPE b gauge
            b{x="1"} 1
            b{x="2"} 2
            """;

        PrometheusSnapshot.Normalize(reordered).ShouldBe(PrometheusSnapshot.Normalize(ordered));
    }

    [Fact]
    public void Histogram_buckets_stay_in_numeric_order()
    {
        var normalized = PrometheusSnapshot.Normalize("""
            # TYPE h histogram
            h_bucket{le="10"} 3
            h_bucket{le="+Inf"} 3
            h_bucket{le="2.5"} 1
            h_sum 7
            h_count 3
            """);

        normalized.ShouldBe("""
            # TYPE h histogram
            h_bucket{le="2.5"} 1
            h_bucket{le="10"} 3
            h_bucket{le="+Inf"} 3
            h_count 3
            h_sum 7

            """.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("gpsd_up 1", "gpsd_up 0")]
    [InlineData("gpsd_tpv_latitude_degrees{device=\"/dev/ttyACM0\"}", "gpsd_tpv_lat_degrees{device=\"/dev/ttyACM0\"}")]
    [InlineData("gpsd_exporter_messages_total{class=\"SKY\"} 7", "gpsd_exporter_messages_total{kind=\"SKY\"} 7")]
    [InlineData("# TYPE gpsd_up gauge", "# TYPE gpsd_up counter")]
    public async Task A_changed_value_name_label_or_type_fails_the_snapshot(string original, string changed)
    {
        SampleExposition.Text.ShouldContain(original);
        var settings = new VerifySettings();
        settings.UseMethodName(nameof(Sample_scrape_matches_the_approved_snapshot));
        settings.DisableDiff();

        var changedText = PrometheusSnapshot.Normalize(SampleExposition.Text.Replace(original, changed, StringComparison.Ordinal));

        try
        {
            await Should.ThrowAsync<VerifyException>(() => Verify(changedText, settings));
        }
        finally
        {
            // Verify writes a .received file next to the snapshot on a mismatch; this one is expected.
            var received = Path.Combine(AttributeReader.GetProjectDirectory(), "Snapshots",
                $"{nameof(PrometheusSnapshotTests)}.{nameof(Sample_scrape_matches_the_approved_snapshot)}.received.txt");
            File.Delete(received);
        }
    }
}
