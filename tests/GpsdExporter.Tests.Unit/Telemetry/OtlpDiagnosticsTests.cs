using System.Diagnostics.Tracing;
using GpsdExporter.Configuration;
using GpsdExporter.Telemetry;
using GpsdExporter.Testing.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Unit.Telemetry;

/// <summary>The startup lines about OTLP, and OTLP exporter failures in the console log.</summary>
public sealed class OtlpDiagnosticsTests : IDisposable
{
    private readonly ListLogger<OtlpDiagnostics> _logger = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-24T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private OtlpDiagnostics? _diagnostics;

    public void Dispose() => _diagnostics?.Dispose();

    [Fact]
    public async Task On_it_logs_where_each_signal_goes_and_any_warning()
    {
        var diagnostics = Create(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318" });

        await diagnostics.StartAsync(TestContext.Current.CancellationToken);

        var entries = _logger.Entries;
        entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message == "OTLP export on: metrics to http://collector:4318/ over grpc");
        entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message == "OTLP export on: logs to http://collector:4318/ over grpc");
        entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("set OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task Off_with_OTEL_variables_it_warns()
    {
        var diagnostics = Create(new() { ["OTEL_SERVICE_NAME"] = "gps" });

        await diagnostics.StartAsync(TestContext.Current.CancellationToken);

        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.StartsWith("OTLP export is off: OTEL_SERVICE_NAME is set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Off_with_nothing_set_it_logs_only_at_debug()
    {
        var diagnostics = Create([]);

        await diagnostics.StartAsync(TestContext.Current.CancellationToken);

        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
    }

    [Fact]
    public void Errors_and_warnings_are_logged_at_their_level()
    {
        var diagnostics = Create([]);

        diagnostics.Forward("2:http://c/v1/metrics", EventLevel.Error, "could not reach the collector");
        diagnostics.Forward("16:http://c/v1/metrics", EventLevel.Warning, "transient error");

        _logger.Entries.Select(e => (e.Level, e.Message)).ShouldBe(
        [
            (LogLevel.Error, "OTLP export failed: could not reach the collector"),
            (LogLevel.Warning, "OTLP export: transient error"),
        ]);
    }

    [Fact]
    public void A_repeated_event_is_logged_once_a_minute_with_a_count_of_the_rest()
    {
        var diagnostics = Create([]);

        diagnostics.Forward("2:http://c/v1/metrics", EventLevel.Error, "failed");
        _time.Advance(TimeSpan.FromSeconds(20));
        diagnostics.Forward("2:http://c/v1/metrics", EventLevel.Error, "failed");
        diagnostics.Forward("2:http://c/v1/logs", EventLevel.Error, "failed for logs");
        _time.Advance(TimeSpan.FromSeconds(30));
        diagnostics.Forward("2:http://c/v1/metrics", EventLevel.Error, "failed");
        _time.Advance(TimeSpan.FromSeconds(10));
        diagnostics.Forward("2:http://c/v1/metrics", EventLevel.Error, "failed");

        _logger.Entries.Select(e => e.Message).ShouldBe(
        [
            "OTLP export failed: failed",
            "OTLP export failed: failed for logs",
            "OTLP export failed: failed (2 more like this since the last one logged)",
        ]);
    }

    [Fact]
    public void Format_fills_in_the_payload_and_drops_stack_traces()
    {
        var text = OtlpDiagnostics.Format(
            2,
            "FailedToReachCollector",
            "Exporter failed send data to collector to {0} endpoint. Data will not be sent. Exception: {1}",
            ["http://c:4318/v1/metrics", "System.Net.Http.HttpRequestException: Connection refused (c:4318)\n   at Somewhere()"]);

        text.ShouldBe("Exporter failed send data to collector to http://c:4318/v1/metrics endpoint. Data will not be sent. Exception: System.Net.Http.HttpRequestException: Connection refused (c:4318)");
    }

    [Fact]
    public void Format_without_a_message_uses_the_event_name()
    {
        OtlpDiagnostics.Format(43, "RetryDeadlineExceeded", null, null).ShouldBe("RetryDeadlineExceeded");
        OtlpDiagnostics.Format(43, null, null, null).ShouldBe("Event 43");
    }

    [Fact]
    public void Key_is_the_event_id_and_endpoint()
    {
        OtlpDiagnostics.Key(2, ["http://c/v1/metrics", "boom"]).ShouldBe("2:http://c/v1/metrics");
        OtlpDiagnostics.Key(43, null).ShouldBe("43:");
    }

    private OtlpDiagnostics Create(Dictionary<string, string> standard)
    {
        var loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(OtlpDiagnostics.Category).Returns(_logger);
        _diagnostics = new OtlpDiagnostics(OtlpStatus.From(new OtlpOptions { Standard = standard }), loggers, _time);
        return _diagnostics;
    }
}
