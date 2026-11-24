using System.Collections.Immutable;
using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// Everything the exporter knows from gpsd on the current connection. Immutable, so a scrape
/// can hold one instance while the reader builds the next.
/// </summary>
public sealed record GpsdState(Timed<VersionReport>? Version, ImmutableSortedDictionary<string, DeviceState> Devices)
{
    public static GpsdState Empty { get; } = new(null, ImmutableSortedDictionary.Create<string, DeviceState>(StringComparer.Ordinal));
}
