namespace GpsdExporter.Tests.Metrics.Exposition;

/// <summary>
/// Tests that start the host run one at a time. An OpenTelemetry meter provider subscribes to
/// meters by name across the whole process, so a host's <c>/metrics</c> would also report the
/// gpsd instruments of any other host alive at the same time. In production there is one host.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialHostTests
{
    public const string Name = "Metrics host";
}
