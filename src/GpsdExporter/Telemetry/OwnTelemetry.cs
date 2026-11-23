using GpsdExporter.Configuration;
using GpsdExporter.Metrics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace GpsdExporter.Telemetry;

/// <summary>
/// The exporter's own telemetry over OTLP (AC5.5; spec open questions 7 and 8): logs, the
/// exporter's own metrics, and .NET runtime and process metrics. gpsd metrics never go to
/// OTLP. OpenTelemetry readers see every meter their provider listens to, so own metrics go
/// through a meter provider of their own that never listens to the gpsd meter.
/// </summary>
internal static class OwnTelemetry
{
    public const string ServiceName = "gpsd-exporter";

    /// <summary>Adds OTLP export of own metrics and logs when <paramref name="otlp"/> is on.</summary>
    public static IServiceCollection AddOwnTelemetry(this IServiceCollection services, OtlpOptions otlp)
    {
        ArgumentNullException.ThrowIfNull(otlp);
        if (!otlp.Enabled)
        {
            return services;
        }

        services.AddSingleton(new OwnMetricsExporter(metrics => metrics.AddOtlpExporter(o => Configure(o, otlp, "v1/metrics"))));
        services.AddHostedService<OwnMetricsExport>();
        services.AddOpenTelemetry().WithLogging(
            logging => logging
                .SetResourceBuilder(Resource())
                .AddOtlpExporter(o => Configure(o, otlp, "v1/logs")),
            options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            });
        return services;
    }

    /// <summary>The meters that go to OTLP: the exporter's own, runtime and process. Never gpsd.</summary>
    public static MeterProviderBuilder AddOwnMeters(this MeterProviderBuilder metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return metrics
            .AddMeter(ExporterMetrics.MeterName)
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation();
    }

    /// <summary>
    /// <c>service.name</c> and <c>service.version</c>, which <c>OTEL_SERVICE_NAME</c> and
    /// <c>OTEL_RESOURCE_ATTRIBUTES</c> can override.
    /// </summary>
    public static ResourceBuilder Resource() => ResourceBuilder.CreateEmpty()
        .AddTelemetrySdk()
        .AddService(ServiceName, serviceVersion: BuildInfo.Version)
        .AddEnvironmentVariableDetector();

    /// <summary>
    /// Applies <c>Otlp:Endpoint</c> when it is set. With <c>http/protobuf</c> the SDK uses an
    /// endpoint set in code as is, so the signal's path is added, as it would be for
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
    /// </summary>
    internal static void Configure(OtlpExporterOptions exporter, OtlpOptions otlp, string signalPath)
    {
        if (string.IsNullOrEmpty(otlp.Endpoint))
        {
            return;
        }

        var endpoint = new Uri(otlp.Endpoint, UriKind.Absolute);
        exporter.Endpoint = exporter.Protocol == OtlpExportProtocol.HttpProtobuf
            ? new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/" + signalPath)
            : endpoint;
    }
}

/// <summary>How own metrics leave the process: OTLP in production, in memory in tests.</summary>
internal sealed class OwnMetricsExporter(Action<MeterProviderBuilder> addExporter)
{
    public void AddTo(MeterProviderBuilder metrics) => addExporter(metrics);
}

/// <summary>The meter provider for own metrics, alive while the host runs.</summary>
internal sealed class OwnMetricsExport(OwnMetricsExporter exporter) : IHostedService, IDisposable
{
    private MeterProvider? _provider;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var metrics = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(OwnTelemetry.Resource())
            .AddOwnMeters();
        exporter.AddTo(metrics);
        _provider = metrics.Build();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _provider?.ForceFlush();
        return Task.CompletedTask;
    }

    public void Dispose() => _provider?.Dispose();
}
