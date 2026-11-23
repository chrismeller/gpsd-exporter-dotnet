namespace GpsdExporter.Tests.Release;

public sealed class ScaffoldTests
{
    // Release smoke tests run against a published binary named by GPSD_EXPORTER_BINARY.
    // Outside the release workflow the variable is unset and the tests are skipped.
    [Fact]
    public void Published_binary_exists_when_configured()
    {
        var path = Environment.GetEnvironmentVariable("GPSD_EXPORTER_BINARY");
        Assert.SkipWhen(string.IsNullOrEmpty(path), "GPSD_EXPORTER_BINARY is not set; release smoke tests run only in the release workflow.");

        File.Exists(path).ShouldBeTrue();
    }
}
