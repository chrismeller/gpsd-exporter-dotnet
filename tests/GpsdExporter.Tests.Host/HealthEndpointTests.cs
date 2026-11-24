using System.Diagnostics;
using System.Net;
using GpsdExporter.Connection;
using GpsdExporter.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GpsdExporter.Tests.Host;

public sealed class HealthEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<bool, HttpStatusCode, HttpStatusCode> Cases => new()
    {
        // Liveness never looks at gpsd; readiness does.
        { true, HttpStatusCode.OK, HttpStatusCode.OK },
        { false, HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Healthz_and_readyz_report_gpsd_as_expected(bool gpsdListening, HttpStatusCode healthz, HttpStatusCode readyz)
    {
        // Not started, FakeGpsd still holds its port, so connections to it are refused.
        await using var gpsd = new FakeGpsd();
        if (gpsdListening)
        {
            gpsd.Start();
        }

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(web => web.UseSetting("Gpsd:Address", gpsd.Address));
        using var client = factory.CreateClient();
        var stats = factory.Services.GetRequiredService<ConnectionStats>();

        await WaitUntilAsync(() => gpsdListening ? stats.Up : stats.TotalAttempts > 0);

        (await StatusAsync(client, "/healthz")).ShouldBe(healthz);
        (await StatusAsync(client, "/readyz")).ShouldBe(readyz);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        return response.StatusCode;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            stopwatch.Elapsed.ShouldBeLessThan(FakeGpsd.DefaultTimeout, "the exporter did not reach gpsd in time");
            await Task.Delay(TimeSpan.FromMilliseconds(20), Ct);
        }
    }
}
