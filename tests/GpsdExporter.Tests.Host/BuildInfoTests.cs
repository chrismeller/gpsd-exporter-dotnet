namespace GpsdExporter.Tests.Host;

public sealed class BuildInfoTests
{
    [Theory]
    [InlineData("1.2.3+abc123", "1.2.3", "abc123")]
    [InlineData("1.2.3-rc.1+abc123", "1.2.3-rc.1", "abc123")]
    [InlineData("1.2.3", "1.2.3", BuildInfo.UnknownRevision)]
    [InlineData("1.2.3+", "1.2.3", BuildInfo.UnknownRevision)]
    public void Informational_version_splits_into_version_and_revision(string informational, string version, string revision) =>
        BuildInfo.Split(informational).ShouldBe((version, revision));

    [Fact]
    public void Build_info_is_populated()
    {
        BuildInfo.Version.ShouldNotBeNullOrWhiteSpace();
        BuildInfo.Version.ShouldNotContain('+');
        BuildInfo.Revision.ShouldNotBeNullOrWhiteSpace();
        BuildInfo.Runtime.ShouldStartWith(".NET ");
    }
}
