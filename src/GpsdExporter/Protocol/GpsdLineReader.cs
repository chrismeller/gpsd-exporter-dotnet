using System.Buffers;
using System.IO.Pipelines;

namespace GpsdExporter.Protocol;

/// <summary>Receives the lines <see cref="GpsdLineReader"/> splits out of the gpsd stream.</summary>
internal interface IGpsdLineHandler
{
    /// <summary>
    /// A complete line, without its LF or CR-LF terminator. The span is only valid for the
    /// duration of the call.
    /// </summary>
    void OnLine(ReadOnlySpan<byte> line);

    /// <summary>
    /// A line longer than the limit was discarded (spec AC2.1, <c>reason="too_long"</c>).
    /// <paramref name="length"/> is the number of bytes dropped, excluding the terminator.
    /// </summary>
    void OnLineTooLong(long length);
}

/// <summary>How a <see cref="GpsdLineReader.ReadAllAsync"/> call ended.</summary>
internal enum GpsdStreamEnd
{
    /// <summary>The stream ended on a line boundary.</summary>
    Closed,

    /// <summary>The stream ended part-way through a line, which was discarded unparsed.</summary>
    ClosedMidLine,
}

/// <summary>
/// Splits a gpsd byte stream into lines (spec R2, AC2.1).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Lines end in LF or CR-LF; the terminator is not passed on. Empty lines are skipped.</item>
/// <item>A line longer than <see cref="MaxLineLength"/> bytes is discarded up to the next LF and
/// reported through <see cref="IGpsdLineHandler.OnLineTooLong"/>. It is never buffered whole:
/// once a pending line passes the limit, its bytes are dropped as they arrive. gpsd's own limit
/// is 10,240 characters, so the cap only guards memory.</item>
/// <item>A line left incomplete when the stream ends is discarded, not parsed.</item>
/// <item>Lines that arrive in one buffer segment are handed over as a slice of the pipe's
/// buffer with no copy; a line split across segments is copied once into a pooled buffer.</item>
/// </list>
/// </remarks>
internal sealed class GpsdLineReader(int maxLineLength = GpsdLineReader.DefaultMaxLineLength)
{
    /// <summary>64 KiB, per AC2.1.</summary>
    public const int DefaultMaxLineLength = 64 * 1024;

    public int MaxLineLength { get; } = maxLineLength > 0
        ? maxLineLength
        : throw new ArgumentOutOfRangeException(nameof(maxLineLength), maxLineLength, "Must be positive.");

    /// <summary>
    /// Reads lines until the stream ends, calling <paramref name="handler"/> for each one.
    /// Cancellation throws <see cref="OperationCanceledException"/>. The reader is completed
    /// before this method returns or throws.
    /// </summary>
    public async Task<GpsdStreamEnd> ReadAllAsync(PipeReader reader, IGpsdLineHandler handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(handler);

        // Bytes of an over-long line dropped so far; -1 while not discarding.
        long discarded = -1;
        Exception? failure = null;
        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;
                var consumed = Process(buffer, handler, ref discarded);

                if (result.IsCompleted)
                {
                    // Whatever is left has no terminator: a partial line, discarded unparsed.
                    var midLine = !consumed.IsEmpty || discarded >= 0;
                    // A fragment already past the limit is reported as too long, the same as it
                    // would be had it arrived in smaller pieces.
                    if (discarded >= 0)
                    {
                        handler.OnLineTooLong(discarded + consumed.Length);
                    }
                    else if (consumed.Length > MaxLineLength)
                    {
                        handler.OnLineTooLong(consumed.Length);
                    }

                    reader.AdvanceTo(result.Buffer.End);
                    return midLine ? GpsdStreamEnd.ClosedMidLine : GpsdStreamEnd.Closed;
                }

                if (discarded >= 0)
                {
                    // Drop the pending bytes of the over-long line instead of buffering them.
                    discarded += consumed.Length;
                    reader.AdvanceTo(result.Buffer.End);
                }
                else if (consumed.Length > MaxLineLength + 1)
                {
                    // Past the limit even allowing for a CR still waiting for its LF.
                    discarded = consumed.Length;
                    reader.AdvanceTo(result.Buffer.End);
                }
                else
                {
                    // Keep the incomplete line, and ask for more data before the next read.
                    reader.AdvanceTo(consumed.Start, result.Buffer.End);
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            await reader.CompleteAsync(failure).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Hands every complete line in <paramref name="buffer"/> to the handler, and returns the
    /// unterminated remainder.
    /// </summary>
    private ReadOnlySequence<byte> Process(ReadOnlySequence<byte> buffer, IGpsdLineHandler handler, ref long discarded)
    {
        var remaining = buffer;
        while (true)
        {
            var newline = remaining.PositionOf((byte)'\n');
            if (newline is null)
            {
                return remaining;
            }

            var line = remaining.Slice(0, newline.Value);
            remaining = remaining.Slice(remaining.GetPosition(1, newline.Value));

            if (discarded >= 0)
            {
                // The tail of an over-long line: report it once, then resume normal lines.
                handler.OnLineTooLong(discarded + TrimCr(line).Length);
                discarded = -1;
                continue;
            }

            line = TrimCr(line);
            if (line.Length > MaxLineLength)
            {
                handler.OnLineTooLong(line.Length);
            }
            else if (!line.IsEmpty)
            {
                Deliver(line, handler);
            }
        }
    }

    private static ReadOnlySequence<byte> TrimCr(ReadOnlySequence<byte> line)
    {
        if (line.IsEmpty)
        {
            return line;
        }

        var last = line.Slice(line.Length - 1);
        return last.FirstSpan[0] == (byte)'\r' ? line.Slice(0, line.Length - 1) : line;
    }

    private static void Deliver(ReadOnlySequence<byte> line, IGpsdLineHandler handler)
    {
        if (line.IsSingleSegment)
        {
            handler.OnLine(line.FirstSpan);
            return;
        }

        var length = (int)line.Length;
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            line.CopyTo(rented);
            handler.OnLine(rented.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
