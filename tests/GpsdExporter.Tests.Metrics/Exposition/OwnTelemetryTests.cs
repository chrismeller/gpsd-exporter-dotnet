using GpsdExporter.Metrics;
using GpsdExporter.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace GpsdExporter.Tests.Metrics.Exposition;

/// <summary>
/// The exporter's own telemetry (EXT-898; spec open questions 7 and 8): with OTLP on, own
/// metrics and runtime and process metrics are exported and gpsd metrics never are. An
/// in-memory exporter stands in for OTLP; everything else is the real host.
/// </summary>
[Collection(SerialHostTests.Name)]
public sealed class OwnTelemetryTests
{
    private static readonly Dictionary<string, string> OtlpOn = new() { ["Otlp:Endpoint"] = "http://127.0.0.1:4317" };

    /// <summary>Prefixes of the gpsd metrics, which must never leave over OTLP.</summary>
    private static readonly string[] GpsdPrefixes =
        ["gpsd_tpv_", "gpsd_sky_", "gpsd_satellite", "gpsd_pps_", "gpsd_toff_", "gpsd_osc_", "gpsd_device_", "gpsd_version_"];

    [Fact]
    public async Task With_OTLP_on_own_and_runtime_metrics_are_exported_and_gpsd_metrics_are_not()
    {
        var exported = new List<MetricSnapshot>();
        var host = new MetricsHost(settings: OtlpOn, configureServices: services =>
        {
            services.RemoveAll<OwnMetricsExporter>();
            services.AddSingleton(new OwnMetricsExporter(metrics => metrics.AddInMemoryExporter(exported)));
        });
        await using (host)
        {
            host.Replay("session-3d-fix.jsonl");
            var scrape = await host.ScrapeAsync();

            // The gpsd metrics are there to be exported, on /metrics.
            scrape.Families.ShouldContain(f => f.Name.StartsWith("gpsd_tpv_", StringComparison.Ordinal));
            scrape.Families.ShouldContain(f => f.Name.StartsWith("gpsd_satellite", StringComparison.Ordinal));
        }

        // Stopping the host flushes the own-metrics provider.
        var names = exported.Select(m => m.Name).Distinct(StringComparer.Ordinal).ToList();
        names.ShouldContain("gpsd_up");
        names.ShouldContain(n => n.StartsWith("gpsd_exporter_", StringComparison.Ordinal));
        names.ShouldContain(n => n.StartsWith("dotnet.", StringComparison.Ordinal));
        names.ShouldContain(n => n.StartsWith("process.", StringComparison.Ordinal));
        names.ShouldNotContain(n => GpsdPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task With_OTLP_on_logs_go_to_OpenTelemetry()
    {
        await using var host = new MetricsHost(settings: OtlpOn, configureServices: services =>
        {
            services.RemoveAll<OwnMetricsExporter>();
            services.AddSingleton(new OwnMetricsExporter(_ => { }));
        });

        host.Services.GetServices<ILoggerProvider>().ShouldContain(p => p is OpenTelemetryLoggerProvider);
        host.Services.GetServices<IHostedService>().ShouldContain(s => s is OwnMetricsExport);
        host.Services.GetServices<IHostedService>().ShouldContain(s => s is OtlpDiagnostics);
    }

    [Fact]
    public async Task OTLP_diagnostics_are_never_sent_over_OTLP()
    {
        await using var host = new MetricsHost(settings: OtlpOn, configureServices: services =>
        {
            services.RemoveAll<OwnMetricsExporter>();
            services.AddSingleton(new OwnMetricsExporter(_ => { }));
        });

        var filters = host.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;
        filters.Rules.ShouldContain(r =>
            r.ProviderName == typeof(OpenTelemetryLoggerProvider).FullName
            && r.CategoryName == OtlpDiagnostics.Category
            && r.LogLevel == LogLevel.None);
    }

    [Fact]
    public async Task With_OTLP_off_nothing_is_exported()
    {
        await using var host = new MetricsHost();

        host.Services.GetServices<ILoggerProvider>().ShouldNotContain(p => p is OpenTelemetryLoggerProvider);
        host.Services.GetServices<IHostedService>().ShouldNotContain(s => s is OwnMetricsExport);
        host.Services.GetService<OwnMetricsExporter>().ShouldBeNull();

        // Still there, to say at startup why OTLP is off when OTEL_ variables are set.
        host.Services.GetServices<IHostedService>().ShouldContain(s => s is OtlpDiagnostics);
    }

    [Fact]
    public async Task Runtime_metrics_are_off_on_metrics_by_default()
    {
        await using var host = new MetricsHost();

        var scrape = await host.ScrapeAsync();

        scrape.Families.ShouldNotContain(f => f.Name.StartsWith("dotnet_", StringComparison.Ordinal));
        scrape.Families.ShouldNotContain(f => f.Name.StartsWith("process_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Metrics_Runtime_puts_runtime_and_process_metrics_on_metrics()
    {
        await using var host = new MetricsHost(
            metrics: new MetricsOptions { Runtime = true },
            settings: new Dictionary<string, string> { ["Metrics:Runtime"] = "true" });

        var scrape = await host.ScrapeAsync();

        scrape.Families.ShouldContain(f => f.Name.StartsWith("dotnet_", StringComparison.Ordinal));
        scrape.Families.ShouldContain(f => f.Name.StartsWith("process_", StringComparison.Ordinal));
        scrape.Value("gpsd_up").ShouldBe(0);
    }
}
