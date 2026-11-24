using Microsoft.AspNetCore.Mvc.Testing;

namespace GpsdExporter.Tests.Host;

public sealed class HostStartupTests
{
    [Fact]
    public async Task The_host_starts_and_serves_requests()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        // Nothing is mapped at / yet; a 404 shows the pipeline is up and answering.
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
