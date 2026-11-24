using GpsdExporter.Connection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GpsdExporter.Health;

/// <summary>
/// Healthy while gpsd is talking to the exporter: it has sent VERSION on the current
/// connection (<c>gpsd_up</c> 1).
/// </summary>
internal sealed class GpsdHealthCheck(ConnectionStats stats) : IHealthCheck
{
    public const string Name = "gpsd";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(stats.Up
            ? HealthCheckResult.Healthy("gpsd is connected.")
            : HealthCheckResult.Unhealthy("gpsd has not sent VERSION on the current connection."));
}
