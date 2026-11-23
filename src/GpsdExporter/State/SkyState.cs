using System.Collections.Immutable;
using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// A device's SKY state (R4). Scalar fields follow the two-epoch rule of
/// <see cref="Epochs{TField}"/>. A SKY with a <c>satellites</c> array replaces the whole
/// satellite set; a SKY without one (DOP-only) leaves it alone. Immutable.
/// </summary>
/// <param name="Satellites">The latest satellite set, or null before the first SKY with an array.
/// Identities are unique: the parser keeps the last of any duplicates (AC6.5).</param>
internal sealed record SkyState(Epochs<SkyField> Epochs, Timed<ImmutableArray<Satellite>>? Satellites)
{
    /// <summary>The served value of a scalar field.</summary>
    public Timed<double>? this[SkyField field] => Epochs[field];

    /// <summary><c>gpsd_sky_satellites_visible</c>: <c>nSat</c>, or the length of the latest array.</summary>
    public Timed<double>? Visible =>
        Epochs[SkyField.NSat]
        ?? (Satellites is { } s ? new Timed<double>(s.Value.Length, s.ReceivedAt) : null);

    /// <summary><c>gpsd_sky_satellites_used</c>: <c>uSat</c>, or the count with <c>used:true</c> in the latest array.</summary>
    public Timed<double>? Used =>
        Epochs[SkyField.USat]
        ?? (Satellites is { } s ? new Timed<double>(s.Value.Count(x => x.Used == true), s.ReceivedAt) : null);

    /// <summary>Applies one SKY to <paramref name="state"/>, which is null before the device's first SKY.</summary>
    public static SkyState Apply(SkyState? state, SkyReport report, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        var epochs = Epochs<SkyField>.Apply(state?.Epochs, report.Time, report.Fields, receivedAt);
        var satellites = report.Satellites is { } array
            ? new Timed<ImmutableArray<Satellite>>([.. array], receivedAt)
            : state?.Satellites;
        return new SkyState(epochs, satellites);
    }
}
