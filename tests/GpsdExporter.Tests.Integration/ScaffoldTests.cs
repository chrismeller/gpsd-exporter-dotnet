namespace GpsdExporter.Tests.Integration;

public sealed class ScaffoldTests
{
    // Placeholder until FakeGpsd (EXT-884) lands; proves the project builds and runs under MTP.
    [Fact]
    public void Integration_test_project_runs() => typeof(Program).Assembly.GetName().Name.ShouldBe("gpsd-exporter");
}
