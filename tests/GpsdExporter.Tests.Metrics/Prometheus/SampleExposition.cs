namespace GpsdExporter.Tests.Metrics.Prometheus;

/// <summary>A hand-written scrape in the shape the OpenTelemetry Prometheus exporter produces.</summary>
internal static class SampleExposition
{
    public const string Text = """
        # HELP gpsd_up Whether the exporter is connected to gpsd and has received VERSION.
        # TYPE gpsd_up gauge
        gpsd_up 1
        # HELP gpsd_exporter_build_info Build information, always 1.
        # TYPE gpsd_exporter_build_info gauge
        gpsd_exporter_build_info{revision="abc123",runtime=".NET 10.0.15",version="1.2.3"} 1
        # HELP gpsd_exporter_messages_total gpsd objects received.
        # TYPE gpsd_exporter_messages_total counter
        gpsd_exporter_messages_total{class="TPV"} 42
        gpsd_exporter_messages_total{class="SKY"} 7
        # HELP gpsd_device_info A device reported by gpsd.
        # TYPE gpsd_device_info gauge
        gpsd_device_info{device="/dev/ttyACM0",driver="u-blox",subtype="SW \"EXT\" CORE 1.00\\HW\nline2"} 1
        # HELP gpsd_tpv_latitude_degrees Latitude.
        # TYPE gpsd_tpv_latitude_degrees gauge
        gpsd_tpv_latitude_degrees{device="/dev/ttyACM0"} 51.477811
        # HELP gpsd_device_last_report_timestamp_seconds When the exporter last received a report.
        # TYPE gpsd_device_last_report_timestamp_seconds gauge
        gpsd_device_last_report_timestamp_seconds{class="TPV",device="/dev/ttyACM0"} 1795428000.25
        # HELP gpsd_exporter_scrape_duration_seconds Time to collect gpsd metrics.
        # TYPE gpsd_exporter_scrape_duration_seconds histogram
        gpsd_exporter_scrape_duration_seconds_bucket{le="0.005"} 3
        gpsd_exporter_scrape_duration_seconds_bucket{le="0.01"} 4
        gpsd_exporter_scrape_duration_seconds_bucket{le="+Inf"} 4
        gpsd_exporter_scrape_duration_seconds_sum 0.0123
        gpsd_exporter_scrape_duration_seconds_count 4
        # EOF

        """;
}
