namespace GpsdExporter.Protocol;

/// <summary>
/// The gpsd object classes the exporter distinguishes. This is the fixed label set of
/// <c>gpsd_exporter_messages_total{class}</c>: anything else is <see cref="Other"/>,
/// so the network cannot create new label values.
/// </summary>
public enum GpsdClass
{
    Version,
    Devices,
    Device,
    Watch,
    Tpv,
    Sky,
    Pps,
    Toff,
    Osc,
    Error,
    Other,
}

public static class GpsdClassExtensions
{
    /// <summary>Every class, in label order.</summary>
    public static IReadOnlyList<GpsdClass> All { get; } = Enum.GetValues<GpsdClass>();

    /// <summary>The <c>class</c> label value: the gpsd class name, or <c>other</c>.</summary>
    public static string Label(this GpsdClass cls) => cls switch
    {
        GpsdClass.Version => "VERSION",
        GpsdClass.Devices => "DEVICES",
        GpsdClass.Device => "DEVICE",
        GpsdClass.Watch => "WATCH",
        GpsdClass.Tpv => "TPV",
        GpsdClass.Sky => "SKY",
        GpsdClass.Pps => "PPS",
        GpsdClass.Toff => "TOFF",
        GpsdClass.Osc => "OSC",
        GpsdClass.Error => "ERROR",
        _ => "other",
    };

    /// <summary>Maps a gpsd <c>class</c> value; unknown classes are <see cref="GpsdClass.Other"/>.</summary>
    public static GpsdClass FromName(string name) => name switch
    {
        "VERSION" => GpsdClass.Version,
        "DEVICES" => GpsdClass.Devices,
        "DEVICE" => GpsdClass.Device,
        "WATCH" => GpsdClass.Watch,
        "TPV" => GpsdClass.Tpv,
        "SKY" => GpsdClass.Sky,
        "PPS" => GpsdClass.Pps,
        "TOFF" => GpsdClass.Toff,
        "OSC" => GpsdClass.Osc,
        "ERROR" => GpsdClass.Error,
        _ => GpsdClass.Other,
    };
}
