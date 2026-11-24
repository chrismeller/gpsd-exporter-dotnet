using System.Collections.Immutable;

namespace GpsdExporter.Metrics;

/// <summary>
/// The <c>gpsd_exporter_scrape_duration_seconds</c> histogram: cumulative bucket counts, sum
/// and count since the exporter started. Scrapes may run concurrently, so every access is
/// under a lock, held only to update or copy a handful of numbers.
/// </summary>
internal sealed class ScrapeDurations
{
    /// <summary>Upper bounds in seconds, 1 ms to 1 s; the last bucket, <c>+Inf</c>, is the count.</summary>
    public static ImmutableArray<double> Bounds { get; } = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1];

    private readonly Lock _lock = new();
    private readonly long[] _buckets = new long[Bounds.Length];
    private long _count;
    private double _sum;

    public void Record(TimeSpan duration)
    {
        var seconds = duration.TotalSeconds;
        lock (_lock)
        {
            for (var i = 0; i < Bounds.Length; i++)
            {
                if (seconds <= Bounds[i])
                {
                    _buckets[i]++;
                }
            }

            _count++;
            _sum += seconds;
        }
    }

    /// <summary>The cumulative count at each bound, the total count and the sum.</summary>
    public (long[] Buckets, long Count, double Sum) Read()
    {
        lock (_lock)
        {
            return ([.. _buckets], _count, _sum);
        }
    }
}
