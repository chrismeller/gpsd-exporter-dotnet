using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// How long each class's values are served after they were received (AC4.3). Bound from the
/// <c>Staleness</c> configuration section (<c>Staleness:Tpv</c> and so on).
/// </summary>
internal sealed record StalenessOptions
{
    public const string Section = "Staleness";

    public TimeSpan Tpv { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan Sky { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan Pps { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan Toff { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan Osc { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The stale-after setting for a class, or null for classes that do not go stale.</summary>
    public TimeSpan? For(GpsdClass cls) => cls switch
    {
        GpsdClass.Tpv => Tpv,
        GpsdClass.Sky => Sky,
        GpsdClass.Pps => Pps,
        GpsdClass.Toff => Toff,
        GpsdClass.Osc => Osc,
        _ => null,
    };
}
