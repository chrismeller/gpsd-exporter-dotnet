using System.Collections.ObjectModel;
using System.Diagnostics;
using GpsdExporter.Connection;
using GpsdExporter.Metrics;
using GpsdExporter.Protocol;
using GpsdExporter.Telemetry;
using GpsdExporter.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace GpsdExporter.Tests.Host;

/// <summary>
/// The exporter's own telemetry, which goes over OTLP in production. The tests add in-memory
/// exporters to the same logger and meter providers that the OTLP exporter is added to.
/// </summary>
public sealed partial class OwnTelemetryTests : IAsyncDisposable
{
    private static readonly string[] OwnMetricNames =
    [
        "gpsd_up",
        "gpsd_exporter_build_info",
        "gpsd_exporter_connection_attempts",
        "gpsd_exporter_disconnects",
        "gpsd_exporter_messages",
        "gpsd_exporter_parse_errors",
        "gpsd_exporter_reconnect_delay_seconds",
        "gpsd_exporter_connected_timestamp_seconds",
        "gpsd_exporter_last_message_timestamp_seconds",
    ];

    private readonly LockedCollection<LogRecord> _logs = new();
    private readonly List<MetricSnapshot> _metrics = [];
    private readonly FakeGpsd _gpsd = FakeGpsd.CreateStarted();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_log_record_written_through_ILogger_arrives()
    {
        await using var factory = CreateFactory();
        var logger = factory.Services.GetRequiredService<ILogger<OwnTelemetryTests>>();

        LogHello(logger, "gpsd");

        var record = _logs.Snapshot().Where(l => l.CategoryName == typeof(OwnTelemetryTests).FullName).ShouldHaveSingleItem();
        record.FormattedMessage.ShouldBe("Hello gpsd");
        record.LogLevel.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task The_exporters_own_metrics_arrive_and_no_gpsd_metric_does()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await using var connection = await _gpsd.AcceptAsync(Ct);
        var stats = factory.Services.GetRequiredService<ConnectionStats>();
        await connection.SendLinesAsync(
            [
                """{"class":"TPV","device":"/dev/gps0","mode":3,"lat":51.5,"lon":-0.1}""",
                """{"class":"SKY","device":"/dev/gps0","satellites":[{"PRN":5,"used":true,"ss":40}]}""",
            ],
            Ct);
        await WaitUntilAsync(() => stats.Up && stats.Messages(GpsdClass.Sky) > 0);

        // The gpsd series exist; they are on /metrics and nowhere else.
        var scrape = await client.GetStringAsync(new Uri("/metrics", UriKind.Relative), Ct);
        scrape.ShouldContain("gpsd_tpv_latitude_degrees{");

        factory.Services.GetRequiredService<MeterProvider>().ForceFlush().ShouldBeTrue();

        _metrics.Select(m => m.MeterName).Distinct().Order(StringComparer.Ordinal).ShouldBe(
            [ExporterMeter.Name, "OpenTelemetry.Instrumentation.Process", "System.Runtime"]);

        var own = _metrics.Where(m => m.MeterName == ExporterMeter.Name).ToList();
        own.Select(m => m.Name).Distinct().ShouldBe(OwnMetricNames, ignoreOrder: true);
        own.Last(m => m.Name == "gpsd_up").MetricPoints.ShouldHaveSingleItem().GetGaugeLastValueLong().ShouldBe(1);

        // Only gpsd_up and gpsd_exporter_* may share a name with a /metrics series.
        var gpsdSeries = MetricCatalogue.All
            .Select(m => m.Name)
            .Where(n => n != MetricCatalogue.Up.Name && !n.StartsWith("gpsd_exporter_", StringComparison.Ordinal))
            .ToHashSet();
        _metrics.Where(m => gpsdSeries.Contains(m.Name)).ShouldBeEmpty();
        _metrics.Where(m => m.Name.StartsWith("gpsd_", StringComparison.Ordinal)).ShouldAllBe(m => m.MeterName == ExporterMeter.Name);
    }

    [Fact]
    public async Task The_service_is_named_after_the_application()
    {
        await using var factory = CreateFactory();

        ServiceName(factory).ShouldBe("gpsd-exporter");
    }

    [Fact]
    public async Task OTEL_SERVICE_NAME_overrides_the_service_name()
    {
        await using var factory = CreateFactory(web => web.UseSetting("OTEL_SERVICE_NAME", "gps-shed"));

        ServiceName(factory).ShouldBe("gps-shed");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hello {Name}")]
    private static partial void LogHello(ILogger logger, string name);

    public async ValueTask DisposeAsync() => await _gpsd.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Gpsd:Address", _gpsd.Address);
            configure?.Invoke(web);
            web.ConfigureTestServices(services =>
            {
                services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(_logs));
                services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(_metrics));
            });
        });

    private static object? ServiceName(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<MeterProvider>().GetResource().Attributes
            .Single(a => a.Key == "service.name").Value;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            stopwatch.Elapsed.ShouldBeLessThan(FakeGpsd.DefaultTimeout, "the exporter did not process gpsd's lines in time");
            await Task.Delay(TimeSpan.FromMilliseconds(20), Ct);
        }
    }

    /// <summary>The host logs from its own threads while a test reads, so adds and reads are locked.</summary>
    private sealed class LockedCollection<T> : Collection<T>
    {
        private readonly Lock _lock = new();

        public T[] Snapshot()
        {
            lock (_lock)
            {
                return [.. this];
            }
        }

        protected override void InsertItem(int index, T item)
        {
            lock (_lock)
            {
                base.InsertItem(index, item);
            }
        }
    }
}
