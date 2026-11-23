using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Release;

/// <summary>
/// T11.1 / AC11.1, AC11.7: the binary runs in a clean <c>debian:12</c> container, which has
/// glibc 2.36 and no .NET, so it is self-contained and runs on Debian 12 and Raspberry Pi OS
/// bookworm.
/// </summary>
public sealed class DebianContainerTests
{
    private const string Target = "/opt/gpsd-exporter";
    private const int Port = 9978;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task T11_1_the_binary_starts_in_a_clean_debian_12_container_and_serves_metrics()
    {
        var binary = new FileInfo(PublishedBinary.Path);
        const string executable = Target + "/gpsd-exporter";
        await using var container = new ContainerBuilder("debian:12")
            .WithResourceMapping(FilePath.Of(binary.FullName), FilePath.Of(executable), fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute
                | UnixFileModes.GroupRead | UnixFileModes.GroupExecute | UnixFileModes.OtherRead | UnixFileModes.OtherExecute)
            .WithEntrypoint(executable)
            .WithCommand("-l", $":{Port}", "-d", "127.0.0.1:1")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(Port).ForPath("/healthz")))
            .Build();

        await container.StartAsync(Ct);

        using var http = new HttpClient { BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(Port)}") };
        var scrape = PrometheusText.Parse(await http.GetStringAsync(new Uri("/metrics", UriKind.Relative), Ct));
        scrape.Value("gpsd_up").ShouldBe(0);
        scrape.All("gpsd_exporter_build_info").ShouldHaveSingleItem().Value.ShouldBe(1);
        scrape.Value("gpsd_exporter_connection_attempts_total", ("result", "refused")).ShouldBeGreaterThanOrEqualTo(1);

        // No .NET in the image: the binary carries its own runtime.
        var dotnet = await container.ExecAsync(["sh", "-c", "command -v dotnet || ls /usr/share/dotnet /usr/lib/dotnet"], Ct);
        dotnet.ExitCode.ShouldNotBe(0);

        var version = await container.ExecAsync([executable, "--version"], Ct);
        version.ExitCode.ShouldBe(0);
        version.Stdout.ShouldStartWith("gpsd-exporter ");
    }
}
