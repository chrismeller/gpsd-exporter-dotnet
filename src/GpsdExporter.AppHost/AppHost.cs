using System.Globalization;

var builder = DistributedApplication.CreateBuilder(args);

// A stand-in gpsd that replays a test fixture, so no GPS receiver is needed.
var gpsd = builder.AddDotnetProject("fake-gpsd", "../../tools/GpsdExporter.FakeGpsd/GpsdExporter.FakeGpsd.csproj")
    .WithEndpoint(name: "gpsd", scheme: "tcp", env: "FAKE_GPSD_PORT");

// The exporter's HTTP endpoint comes from its launch profile. Aspire sets the OTEL_*
// variables itself, so its logs and metrics show up in the dashboard.
var exporter = builder.AddDotnetProject("gpsd-exporter", "../GpsdExporter/GpsdExporter.csproj")
    .WithEnvironment("GPSD_EXPORTER_GPSD__ADDRESS", gpsd.GetEndpoint("gpsd").Property(EndpointProperty.HostAndPort))
    .WithHttpHealthCheck("/health")
    .WaitFor(gpsd);

// Prometheus scrapes the exporter's /metrics. The target is only known once Aspire has
// allocated the exporter's port, so prometheus.yml is written when the container starts.
var exporterFromContainers = exporter.GetEndpoint("http", KnownNetworkIdentifiers.DefaultAspireContainerNetwork);
builder.AddContainer("prometheus", "prom/prometheus", "v3.5.0")
    .WithContainerFiles("/etc/prometheus", async (_, cancellationToken) =>
    {
        var target = await exporterFromContainers.Property(EndpointProperty.HostAndPort)
            .GetValueAsync(cancellationToken).ConfigureAwait(false);
        return [new ContainerFile { Name = "prometheus.yml", Contents = PrometheusConfig(target) }];
    })
    .WithHttpEndpoint(targetPort: 9090)
    .WithHttpHealthCheck("/-/ready")
    .WithReference(exporter.GetEndpoint("http"))
    .WaitFor(exporter);

await builder.Build().RunAsync().ConfigureAwait(false);

static string PrometheusConfig(string? target) => string.Create(CultureInfo.InvariantCulture, $"""
    global:
      scrape_interval: 5s
    scrape_configs:
      - job_name: gpsd-exporter
        static_configs:
          - targets: ["{target}"]
    """);
