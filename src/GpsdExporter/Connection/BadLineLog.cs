using System.Text;
using GpsdExporter.Protocol;

namespace GpsdExporter.Connection;

/// <summary>
/// Rate-limited logging of bad lines (AC2.7). Each one is logged at Debug, cut to
/// <see cref="MaxLoggedChars"/> characters. A Warning summarising the counts by reason is
/// written at most once per <see cref="SummaryInterval"/>: the first bad line is summarised
/// at once, and any that follow within the interval are summarised when it ends. Time comes
/// from the injected <see cref="TimeProvider"/>.
/// </summary>
internal sealed class BadLineLog : IDisposable
{
    public const int MaxLoggedChars = 200;

    public static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private readonly long[] _counts = new long[ParseErrorReasonExtensions.All.Count];
    private long _pending;
    private DateTimeOffset _since;
    private bool _intervalOpen;

    public BadLineLog(ILogger logger, TimeProvider time)
    {
        _logger = logger;
        _time = time;
        _timer = time.CreateTimer(static state => ((BadLineLog)state!).OnIntervalEnd(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>A line was discarded, or one of its fields was, for <paramref name="reason"/>.</summary>
    public void Discarded(ParseErrorReason reason, ReadOnlySpan<byte> line)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            Log.BadLine(_logger, reason.Label(), Excerpt(line));
        }

        Count(reason);
    }

    /// <summary>A line longer than the reader's limit was discarded without being kept.</summary>
    public void DiscardedTooLong(long length)
    {
        Log.LongLine(_logger, length, ParseErrorReason.TooLong.Label());
        Count(ParseErrorReason.TooLong);
    }

    /// <summary>The start of <paramref name="line"/> as text, at most <see cref="MaxLoggedChars"/> characters plus an ellipsis.</summary>
    public static string Excerpt(ReadOnlySpan<byte> line)
    {
        // A UTF-16 character takes at most four bytes, so this is always enough text.
        var head = line[..Math.Min(line.Length, MaxLoggedChars * 4)];
        var text = Encoding.UTF8.GetString(head);
        if (text.Length > MaxLoggedChars)
        {
            var cut = char.IsHighSurrogate(text[MaxLoggedChars - 1]) ? MaxLoggedChars - 1 : MaxLoggedChars;
            return string.Concat(text.AsSpan(0, cut), "…");
        }

        return head.Length < line.Length ? text + "…" : text;
    }

    public void Dispose() => _timer.Dispose();

    private void Count(ParseErrorReason reason)
    {
        lock (_gate)
        {
            if (_pending == 0)
            {
                _since = _time.GetUtcNow();
            }

            _counts[(int)reason]++;
            _pending++;
            if (!_intervalOpen)
            {
                Summarise();
                _intervalOpen = true;
                _timer.Change(SummaryInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnIntervalEnd()
    {
        lock (_gate)
        {
            if (_pending == 0)
            {
                _intervalOpen = false;
                return;
            }

            Summarise();
            _timer.Change(SummaryInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void Summarise()
    {
        var reasons = string.Join(", ", ParseErrorReasonExtensions.All
            .Where(r => _counts[(int)r] > 0)
            .Select(r => $"{r.Label()}={_counts[(int)r]}"));
        Log.BadLineSummary(_logger, _pending, _since, reasons);
        Array.Clear(_counts);
        _pending = 0;
    }
}
