using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// A device's TPV state: fields follow the two-epoch rule of
/// <see cref="Epochs{TField}"/>, and fix loss removes fields at once. Immutable;
/// <see cref="Apply"/> returns a new state.
/// </summary>
public sealed record TpvState(Timed<int> Mode, Epochs<TpvField> Epochs)
{
    /// <summary>Fields served only with a 2D or 3D fix (mode 2 or 3).</summary>
    public static readonly TpvField[] FixFields =
    [
        TpvField.Lat, TpvField.Lon,
        TpvField.Speed, TpvField.Track, TpvField.MagTrack,
        TpvField.VelN, TpvField.VelE, TpvField.VelD,
        TpvField.EcefX, TpvField.EcefY, TpvField.EcefZ,
        TpvField.EcefVx, TpvField.EcefVy, TpvField.EcefVz,
        TpvField.EcefPAcc, TpvField.EcefVAcc,
        TpvField.RelN, TpvField.RelE, TpvField.RelD,
        TpvField.Eph, TpvField.Epy, TpvField.Epx, TpvField.Sep,
        TpvField.Eps, TpvField.Epd,
    ];

    /// <summary>Fields served only with a 3D fix (mode 3): altitude, climb and vertical error.</summary>
    public static readonly TpvField[] ThreeDFields =
    [
        TpvField.AltHae, TpvField.AltMsl, TpvField.Climb, TpvField.Epv, TpvField.Epc,
    ];

    private static readonly TpvField[] NoFixFields = [.. FixFields, .. ThreeDFields];

    public Epoch<TpvField> Current => Epochs.Current;

    public Epoch<TpvField>? Previous => Epochs.Previous;

    /// <summary>The served value of a field.</summary>
    public Timed<double>? this[TpvField field] => Epochs[field];

    /// <summary>Every served field, in enum order.</summary>
    public IEnumerable<(TpvField Field, Timed<double> Value)> Served => Epochs.Served;

    /// <summary>The fix time (<c>gpsd_tpv_timestamp_seconds</c>), served with a 2D or 3D fix only.</summary>
    public Timed<DateTimeOffset>? Timestamp => Mode.Value < 2 ? null : Epochs.Time;

    /// <summary>Applies one TPV to <paramref name="state"/>, which is null before the device's first TPV.</summary>
    public static TpvState Apply(TpvState? state, TpvReport report, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        var epochs = Epochs<TpvField>.Apply(state?.Epochs, report.Time, report.Fields, receivedAt);
        epochs = report.Mode switch
        {
            < 2 => epochs.Without(NoFixFields),
            2 => epochs.Without(ThreeDFields),
            _ => epochs,
        };

        return new TpvState(new Timed<int>(report.Mode, receivedAt), epochs);
    }
}
