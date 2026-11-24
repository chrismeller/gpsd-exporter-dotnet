using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>One epoch: the reports of a class that share a <c>time</c>, merged.</summary>
/// <param name="Time">The epoch's <c>time</c>, or null for an untimed report, which is an epoch of its own.</param>
/// <param name="ReceivedAt">When the latest report of the epoch was received.</param>
public sealed record Epoch<TField>(DateTimeOffset? Time, DateTimeOffset ReceivedAt, TimedFields<TField> Fields)
    where TField : struct, Enum;

/// <summary>
/// The current and previous epoch of a class that gpsd may split across several reports
/// (TPV and the scalar SKY fields). A field is served if it was present in the current
/// epoch or the one before it. Immutable.
/// </summary>
public sealed record Epochs<TField>(Epoch<TField> Current, Epoch<TField>? Previous)
    where TField : struct, Enum
{
    /// <summary>The served value of a field: from the current epoch, else the previous one.</summary>
    public Timed<double>? this[TField field] => Current.Fields[field] ?? Previous?.Fields[field];

    /// <summary>Every served field, in enum order.</summary>
    public IEnumerable<(TField Field, Timed<double> Value)> Served
    {
        get
        {
            foreach (var f in FieldIndex<TField>.All)
            {
                if (this[f] is { } value)
                {
                    yield return (f, value);
                }
            }
        }
    }

    /// <summary>
    /// The served epoch time with when it was received: the current epoch's, else the previous one's.
    /// </summary>
    public Timed<DateTimeOffset>? Time
    {
        get
        {
            var epoch = Current.Time is not null ? Current : Previous;
            return epoch?.Time is { } t ? new Timed<DateTimeOffset>(t, epoch.ReceivedAt) : null;
        }
    }

    /// <summary>
    /// Adds one report to <paramref name="epochs"/>, which is null before the first. A report
    /// with the current epoch's <c>time</c> merges into it. A later time, an earlier time (a
    /// receiver reset) or no time starts a new epoch, and the current one becomes the previous.
    /// </summary>
    public static Epochs<TField> Apply(Epochs<TField>? epochs, DateTimeOffset? time, FieldValues<TField> fields, DateTimeOffset receivedAt)
    {
        if (epochs is not null && time is not null && epochs.Current.Time == time)
        {
            var merged = epochs.Current with { ReceivedAt = receivedAt, Fields = epochs.Current.Fields.Merge(fields, receivedAt) };
            return epochs with { Current = merged };
        }

        return new Epochs<TField>(new Epoch<TField>(time, receivedAt, TimedFields<TField>.From(fields, receivedAt)), epochs?.Current);
    }

    /// <summary>A copy with the given fields removed from both epochs.</summary>
    public Epochs<TField> Without(ReadOnlySpan<TField> fields)
    {
        if (fields.IsEmpty)
        {
            return this;
        }

        return new Epochs<TField>(
            Current with { Fields = Current.Fields.Without(fields) },
            Previous is null ? null : Previous with { Fields = Previous.Fields.Without(fields) });
    }
}
