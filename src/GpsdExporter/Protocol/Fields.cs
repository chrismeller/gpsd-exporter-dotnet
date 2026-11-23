using System.Runtime.CompilerServices;

namespace GpsdExporter.Protocol;

/// <summary>
/// The TPV fields the exporter reads, one per <c>gpsd_tpv_*</c> source field in the spec's
/// TPV table. <c>mode</c> and <c>time</c> are held separately on <see cref="TpvReport"/>.
/// Not read: <c>alt</c> (deprecated), <c>datum</c>, <c>depth</c>, wind, <c>wtemp</c>, <c>magdev</c>.
/// </summary>
internal enum TpvField
{
    Status,
    LeapSeconds,
    Lat,
    Lon,
    AltHae,
    AltMsl,
    GeoidSep,
    Speed,
    Climb,
    Track,
    MagTrack,
    MagVar,
    VelN,
    VelE,
    VelD,
    EcefX,
    EcefY,
    EcefZ,
    EcefVx,
    EcefVy,
    EcefVz,
    EcefPAcc,
    EcefVAcc,
    RelN,
    RelE,
    RelD,
    Eph,
    Epv,
    Epy,
    Epx,
    Sep,
    Ept,
    Eps,
    Epc,
    Epd,
    ClockBias,
    ClockDrift,
    DgpsAge,
    DgpsSta,
    Jam,
    Ant,
    AntPwr,
    Temp,
}

/// <summary>The scalar SKY fields the exporter reads. <c>time</c> is held on <see cref="SkyReport"/>.</summary>
internal enum SkyField
{
    Gdop,
    Hdop,
    Pdop,
    Tdop,
    Vdop,
    Xdop,
    Ydop,
    NSat,
    USat,
}

/// <summary>
/// The per-satellite numeric fields the exporter reads. Identity (<c>PRN</c>, <c>gnssid</c>,
/// <c>svid</c>, <c>sigid</c>) and <c>used</c> are held on <see cref="Satellite"/>.
/// <c>prRes</c> and <c>qual</c> are read here, from satellite objects (spec open question 2).
/// </summary>
internal enum SatelliteField
{
    Az,
    El,
    Ss,
    Health,
    Qual,
    PrRes,
}

/// <summary>
/// An immutable set of optional numeric values keyed by a field enum. A field gpsd did not
/// send is absent, never 0 (AC3.1); a field gpsd sent as 0 is present with value 0 (AC3.2).
/// </summary>
internal readonly struct FieldValues<TField> : IEquatable<FieldValues<TField>>
    where TField : struct, Enum
{
    private static readonly int Count = Enum.GetValues<TField>().Length;

    private readonly double?[]? _values;

    private FieldValues(double?[] values) => _values = values;

    public static FieldValues<TField> Empty => default;

    public double? this[TField field] => _values?[Index(field)];

    public bool Has(TField field) => this[field].HasValue;

    /// <summary>The fields that have a value, in enum order.</summary>
    public IEnumerable<(TField Field, double Value)> Present
    {
        get
        {
            if (_values is null)
            {
                yield break;
            }

            var all = Enum.GetValues<TField>();
            for (var i = 0; i < all.Length; i++)
            {
                if (_values[i] is { } v)
                {
                    yield return (all[i], v);
                }
            }
        }
    }

    public int PresentCount => _values?.Count(v => v.HasValue) ?? 0;

    public bool Equals(FieldValues<TField> other)
    {
        for (var i = 0; i < Count; i++)
        {
            if (_values?[i] != other._values?[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is FieldValues<TField> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < Count; i++)
        {
            hash.Add(_values?[i]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(FieldValues<TField> left, FieldValues<TField> right) => left.Equals(right);

    public static bool operator !=(FieldValues<TField> left, FieldValues<TField> right) => !left.Equals(right);

    public override string ToString() =>
        "{" + string.Join(", ", Present.Select(p => $"{p.Field}={p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}")) + "}";

    private static int Index(TField field)
    {
        var i = Unsafe.As<TField, int>(ref field);
        return (uint)i < (uint)Count ? i : throw new ArgumentOutOfRangeException(nameof(field), field, null);
    }

    /// <summary>Accumulates values, then produces an immutable <see cref="FieldValues{TField}"/>.</summary>
    public sealed class Builder
    {
        private double?[]? _values;

        public Builder Set(TField field, double value)
        {
            _values ??= new double?[Count];
            _values[Index(field)] = value;
            return this;
        }

        public FieldValues<TField> Build()
        {
            var built = _values is null ? default : new FieldValues<TField>(_values);
            _values = null;
            return built;
        }
    }
}
