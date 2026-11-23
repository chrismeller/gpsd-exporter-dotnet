using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// How long each class's values are served after they were received (AC4.3). Bound from the
/// <c>Staleness</c> configuration section (<c>Staleness:Tpv</c> and so on).
/// </summary>
internal sealed record StalenessOptions
{
    public const string Section = "Staleness";

    public TimeSpan Tpv { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan Sky { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan Pps { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan Toff { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan Osc { get; set; } = TimeSpan.FromSeconds(30);

    public IReadOnlyList<string> Validate() =>
        [.. new[] { (Tpv, nameof(Tpv)), (Sky, nameof(Sky)), (Pps, nameof(Pps)), (Toff, nameof(Toff)), (Osc, nameof(Osc)) }
            .Where(s => s.Item1 <= TimeSpan.Zero)
            .Select(s => $"{Section}:{s.Item2} must be positive.")];

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
