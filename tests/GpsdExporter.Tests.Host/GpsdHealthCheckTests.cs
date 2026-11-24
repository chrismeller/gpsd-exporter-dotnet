using GpsdExporter.Connection;
using GpsdExporter.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GpsdExporter.Tests.Host;

public sealed class GpsdHealthCheckTests
{
    [Fact]
    public async Task Healthy_once_gpsd_has_sent_VERSION()
    {
        var stats = new ConnectionStats();
        stats.Connected(DateTimeOffset.UnixEpoch);
        stats.VersionReceived();

        var result = await CheckAsync(stats);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Unhealthy_before_the_first_connection()
    {
        var result = await CheckAsync(new ConnectionStats());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Unhealthy_when_connected_but_no_VERSION_yet()
    {
        var stats = new ConnectionStats();
        stats.Connected(DateTimeOffset.UnixEpoch);

        var result = await CheckAsync(stats);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Unhealthy_after_gpsd_disconnects()
    {
        var stats = new ConnectionStats();
        stats.Connected(DateTimeOffset.UnixEpoch);
        stats.VersionReceived();
        stats.Disconnected(DisconnectReason.Closed);

        var result = await CheckAsync(stats);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    private static Task<HealthCheckResult> CheckAsync(ConnectionStats stats)
    {
        var check = new GpsdHealthCheck(stats);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(GpsdHealthCheck.Name, check, HealthStatus.Unhealthy, ["ready"]),
        };
        return check.CheckHealthAsync(context, TestContext.Current.CancellationToken);
    }
}
