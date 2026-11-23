namespace GpsdExporter.Testing;

public sealed class FakeGpsdOptions
{
    /// <summary>
    /// Real gpsd sends VERSION as soon as a client connects. Set to false to accept and send
    /// nothing at all (a stalled gpsd).
    /// </summary>
    public bool SendVersionOnConnect { get; init; } = true;

    public string VersionLine { get; init; } = FakeGpsd.DefaultVersionLine;
}
