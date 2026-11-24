using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using GpsdExporter.Configuration;
using GpsdExporter.Telemetry;
using GpsdExporter.Testing.Logging;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

namespace GpsdExporter.Tests.Integration.Telemetry;

/// <summary>
/// A real OTLP exporter that cannot reach its collector: the failure, which the OpenTelemetry
/// SDK reports only on its event source, reaches the exporter's log.
/// </summary>
public sealed class OtlpDiagnosticsExportTests
{
    [Fact]
    public async Task A_collector_that_cannot_be_reached_is_logged_as_an_error()
    {
        var endpoint = new Uri($"http://127.0.0.1:{UnusedPort()}/v1/metrics");
        var logger = new ListLogger<OtlpDiagnostics>();
        var loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(OtlpDiagnostics.Category).Returns(logger);
        var status = OtlpStatus.From(new OtlpOptions { Endpoint = "http://127.0.0.1:1" });
        using var diagnostics = new OtlpDiagnostics(status, loggers, TimeProvider.System);
        await diagnostics.StartAsync(TestContext.Current.CancellationToken);

        using var meter = new Meter($"{nameof(OtlpDiagnosticsExportTests)}.{Guid.NewGuid():N}");
        using (var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(meter.Name)
            .AddOtlpExporter(o =>
            {
                o.Protocol = OtlpExportProtocol.HttpProtobuf;
                o.Endpoint = endpoint;
                o.TimeoutMilliseconds = 2000;
            })
            .Build())
        {
            meter.CreateCounter<long>("probe").Add(1);
            provider.ForceFlush(5000);
        }

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Error
            && e.Message.StartsWith("OTLP export failed: ", StringComparison.Ordinal)
            && e.Message.Contains(endpoint.AbsoluteUri, StringComparison.Ordinal));
    }

    private static int UnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
