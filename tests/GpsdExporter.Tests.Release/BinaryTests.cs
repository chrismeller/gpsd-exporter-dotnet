using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Release;

/// <summary>The published binary on the runner itself (T11.2, AC5.6).</summary>
public sealed class BinaryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Version_prints_the_version_and_exits_0()
    {
        using var process = PublishedBinary.Start("--version");
        var stdout = await process.StandardOutput.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);

        process.ExitCode.ShouldBe(0);
        stdout.ShouldStartWith("gpsd-exporter ");
        stdout.ShouldContain(".NET");
    }

    /// <summary>
    /// systemd starts the exporter in /. It must still open its port at once: watching
    /// appsettings.json would put an inotify watch on every directory under /, which delayed
    /// startup by minutes on a CI runner.
    /// </summary>
    [Fact]
    public async Task It_starts_quickly_with_the_root_directory_as_its_working_directory()
    {
        var port = FreePort();
        using var process = PublishedBinary.StartIn("/", "-l", $"127.0.0.1:{port}");
        var output = new OutputCollector(process);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                try
                {
                    (await http.GetStringAsync(new Uri("/healthz", UriKind.Relative), Ct)).ShouldBe("ok\n");
                    break;
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50, Ct);
                }
                catch (HttpRequestException e)
                {
                    throw new TimeoutException($"/healthz did not answer within 5 seconds.\nOutput:\n{output.Text}", e);
                }
            }
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task T11_2_Go_flags_against_FakeGpsd_serve_metrics()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var port = Scraping.FreePort();
        using var process = PublishedBinary.Start("-d", gpsd.Address, "-l", $"127.0.0.1:{port}", "-p", "10s");
        var output = new OutputCollector(process);
        try
        {
            var connection = await gpsd.AcceptAsync(Ct);
            await connection.WaitForLineAsync("""?WATCH={"enable":true,"json":true,"pps":true};""", Ct);
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await Scraping.Until(http, s => s.Find("gpsd_up")?.Value == 1, "gpsd_up is 1", Ct);

            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
            await connection.SendLineAsync($$"""{"class":"TPV","device":"/dev/ttyACM0","mode":3,"time":"{{now}}","lat":51.5,"lon":-0.1}""", Ct);
            var scrape = await Scraping.Until(http, s => s.Has("gpsd_tpv_latitude_degrees", ("device", "/dev/ttyACM0")), "the TPV is served", Ct);

            scrape.Value("gpsd_tpv_latitude_degrees", ("device", "/dev/ttyACM0")).ShouldBe(51.5);
            scrape.Value("gpsd_tpv_mode", ("device", "/dev/ttyACM0")).ShouldBe(3);
            scrape.All("gpsd_exporter_build_info").ShouldHaveSingleItem().Value.ShouldBe(1);
            output.Text.ShouldContain("-p is ignored");
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException($"{e.Message}\nExporter output:\n{output.Text}", e);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
