using System.Globalization;
using DotNet.Testcontainers.Builders;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Release;

/// <summary>
/// T10.1: the container image named by <c>GPSD_EXPORTER_IMAGE</c>, started through
/// Testcontainers on the runner's own architecture, serves <c>/metrics</c> against FakeGpsd.
/// It runs with host networking, as the README's example does, so it reaches FakeGpsd on
/// 127.0.0.1. Skipped unless the variable is set (the image workflow sets it).
/// </summary>
public sealed class ImageTests
{
    public const string Variable = "GPSD_EXPORTER_IMAGE";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task T10_1_the_image_serves_metrics_against_FakeGpsd()
    {
        var image = Environment.GetEnvironmentVariable(Variable);
        Assert.SkipWhen(string.IsNullOrEmpty(image), $"{Variable} is not set; image tests run only in the image workflow.");

        await using var gpsd = FakeGpsd.CreateStarted();
        var port = Scraping.FreePort();
        await using var container = new ContainerBuilder(image!)
            .WithCommand("-d", gpsd.Address, "-l", $"127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}")
            .WithCreateParameterModifier(parameters => (parameters.HostConfig ??= new()).NetworkMode = "host")
            .Build();

        await container.StartAsync(Ct);
        var connection = await gpsd.AcceptAsync(Ct);
        await connection.WaitForLineAsync("""?WATCH={"enable":true,"json":true,"pps":true};""", Ct);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        await Scraping.Until(http, s => s.Find("gpsd_up")?.Value == 1, "gpsd_up is 1", Ct);

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        await connection.SendLineAsync($$"""{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"{{now}}","lat":51.5,"lon":-0.1}""", Ct);
        var scrape = await Scraping.Until(http, s => s.Has("gpsd_tpv_latitude_degrees", ("device", "/dev/ttyACM0")), "the TPV is served", Ct);

        scrape.Value("gpsd_tpv_latitude_degrees", ("device", "/dev/ttyACM0")).ShouldBe(51.5);
        scrape.All("gpsd_exporter_build_info").ShouldHaveSingleItem().Value.ShouldBe(1);
    }
}
