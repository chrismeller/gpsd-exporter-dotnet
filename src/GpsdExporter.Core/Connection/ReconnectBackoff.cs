namespace GpsdExporter.Connection;

/// <summary>
/// The reconnect schedule: the initial delay, doubling up to the maximum, each with
/// ±20% jitter so that many exporters do not reconnect in step. <see cref="Reset"/> after a
/// connection has delivered VERSION.
/// </summary>
public sealed class ReconnectBackoff
{
    public const double Jitter = 0.2;

    private readonly TimeSpan _initial;
    private readonly TimeSpan _max;
    private readonly Func<double> _random;
    private TimeSpan _next;

    /// <param name="random">Returns a value in [0, 1); <see cref="Random.Shared"/> by default.</param>
    public ReconnectBackoff(TimeSpan initial, TimeSpan max, Func<double>? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initial, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, initial);
        _initial = initial;
        _max = max;
        _random = random ?? Random.Shared.NextDouble;
        _next = initial;
    }

    /// <summary>The delay before the next attempt; each call moves along the schedule.</summary>
    public TimeSpan Next()
    {
        var delay = _next;
        _next = delay * 2 < _max ? delay * 2 : _max;
        return delay * (1 + (Jitter * ((2 * _random()) - 1)));
    }

    public void Reset() => _next = _initial;
}
