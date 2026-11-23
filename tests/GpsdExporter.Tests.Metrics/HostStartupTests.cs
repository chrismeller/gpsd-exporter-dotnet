using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GpsdExporter.Tests.Metrics;

[Collection(Exposition.SerialHostTests.Name)]
public sealed class HostStartupTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Host_starts_in_process_and_answers_http()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
