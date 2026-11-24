namespace GpsdExporter.Protocol;

/// <summary>
/// The label set of <c>gpsd_exporter_parse_errors_total{reason}</c>, fixed so the series count stays bounded.
/// The parser reports <see cref="InvalidJson"/> to <see cref="BadField"/>; the line reader
/// reports <see cref="TooLong"/>; the state store reports the device and satellite reasons.
/// </summary>
public enum ParseErrorReason
{
    TooLong,
    InvalidJson,
    NotObject,
    MissingClass,
    MissingField,
    BadField,
    MissingDevice,
    TooManyDevices,
    DuplicateSatellite,
}

public static class ParseErrorReasonExtensions
{
    public static IReadOnlyList<ParseErrorReason> All { get; } = Enum.GetValues<ParseErrorReason>();

    public static string Label(this ParseErrorReason reason) => reason switch
    {
        ParseErrorReason.TooLong => "too_long",
        ParseErrorReason.InvalidJson => "invalid_json",
        ParseErrorReason.NotObject => "not_object",
        ParseErrorReason.MissingClass => "missing_class",
        ParseErrorReason.MissingField => "missing_field",
        ParseErrorReason.BadField => "bad_field",
        ParseErrorReason.MissingDevice => "missing_device",
        ParseErrorReason.TooManyDevices => "too_many_devices",
        ParseErrorReason.DuplicateSatellite => "duplicate_satellite",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
