using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// An immutable set of optional numeric values keyed by a field enum, each with the time it
/// was received. Changes return a new instance; an instance is never modified, so it can be
/// shared with a scrape that is reading it.
/// </summary>
internal readonly struct TimedFields<TField>
    where TField : struct, Enum
{
    private readonly Timed<double>?[]? _values;

    private TimedFields(Timed<double>?[] values) => _values = values;

    public static TimedFields<TField> Empty => default;

    public Timed<double>? this[TField field] => _values?[FieldIndex<TField>.Of(field)];

    public bool IsEmpty => _values is null || Array.TrueForAll(_values, v => v is null);

    /// <summary>The fields that have a value, in enum order.</summary>
    public IEnumerable<(TField Field, Timed<double> Value)> Present
    {
        get
        {
            if (_values is null)
            {
                yield break;
            }

            var all = FieldIndex<TField>.All;
            for (var i = 0; i < all.Length; i++)
            {
                if (_values[i] is { } v)
                {
                    yield return (all[i], v);
                }
            }
        }
    }

    public static TimedFields<TField> From(FieldValues<TField> values, DateTimeOffset receivedAt) =>
        Empty.Merge(values, receivedAt);

    /// <summary>A copy with every field present in <paramref name="values"/> set or replaced.</summary>
    public TimedFields<TField> Merge(FieldValues<TField> values, DateTimeOffset receivedAt)
    {
        if (values.PresentCount == 0)
        {
            return this;
        }

        var copy = _values is null ? new Timed<double>?[FieldIndex<TField>.Count] : (Timed<double>?[])_values.Clone();
        foreach (var (field, value) in values.Present)
        {
            copy[FieldIndex<TField>.Of(field)] = new Timed<double>(value, receivedAt);
        }

        return new TimedFields<TField>(copy);
    }

    /// <summary>A copy without the given fields.</summary>
    public TimedFields<TField> Without(ReadOnlySpan<TField> fields)
    {
        if (_values is null)
        {
            return this;
        }

        Timed<double>?[]? copy = null;
        foreach (var field in fields)
        {
            var i = FieldIndex<TField>.Of(field);
            if (_values[i] is not null)
            {
                copy ??= (Timed<double>?[])_values.Clone();
                copy[i] = null;
            }
        }

        return copy is null ? this : new TimedFields<TField>(copy);
    }
}
