using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Unit;

public sealed class ScaffoldTests
{
    [Fact]
    public void Fake_time_provider_advances_without_waiting()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);

        time.Advance(TimeSpan.FromSeconds(30));

        time.GetUtcNow().ShouldBe(DateTimeOffset.UnixEpoch.AddSeconds(30));
    }
}
