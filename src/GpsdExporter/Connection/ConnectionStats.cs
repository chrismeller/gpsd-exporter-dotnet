using GpsdExporter.Protocol;

namespace GpsdExporter.Connection;

/// <summary>The <c>result</c> label of <c>gpsd_exporter_connection_attempts_total</c>.</summary>
internal enum ConnectResult
{
    Success,
    Refused,
    Timeout,
    Error,
}

/// <summary>The <c>reason</c> label of <c>gpsd_exporter_disconnects_total</c>.</summary>
internal enum DisconnectReason
{
    /// <summary>gpsd closed the connection.</summary>
    Closed,

    /// <summary>A socket error.</summary>
    Error,

    /// <summary>Nothing arrived after the keepalive probe (AC1.4).</summary>
    KeepaliveTimeout,

    /// <summary>The exporter is stopping.</summary>
    Shutdown,
}

internal static class ConnectionLabels
{
    public static string Label(this ConnectResult result) => result switch
    {
        ConnectResult.Success => "success",
        ConnectResult.Refused => "refused",
        ConnectResult.Timeout => "timeout",
        _ => "error",
    };

    public static string Label(this DisconnectReason reason) => reason switch
    {
        DisconnectReason.Closed => "closed",
        DisconnectReason.Error => "error",
        DisconnectReason.KeepaliveTimeout => "keepalive_timeout",
        _ => "shutdown",
    };
}

/// <summary>
/// The connection's own health figures, which become the exporter metrics (spec R5). Written
/// by the connection manager, read by scrapes on other threads; every member is thread-safe.
/// Counters keep counting across connections.
/// </summary>
internal sealed class ConnectionStats
{
    private readonly long[] _attempts = new long[Enum.GetValues<ConnectResult>().Length];
    private readonly long[] _disconnects = new long[Enum.GetValues<DisconnectReason>().Length];
    private readonly long[] _messages = new long[GpsdClassExtensions.All.Count];
    private readonly long[] _parseErrors = new long[ParseErrorReasonExtensions.All.Count];
    private long _reconnectDelayTicks;
    private long _connectedAtTicks = long.MinValue;
    private long _lastMessageTicks = long.MinValue;
    private int _up;

    /// <summary><c>gpsd_up</c>: VERSION received on the current connection (AC5.1).</summary>
    public bool Up => Volatile.Read(ref _up) != 0;

    /// <summary>The backoff delay being waited out; zero while connected or connecting.</summary>
    public TimeSpan ReconnectDelay => TimeSpan.FromTicks(Interlocked.Read(ref _reconnectDelayTicks));

    /// <summary>When the current connection was established; null while disconnected.</summary>
    public DateTimeOffset? ConnectedAt => FromTicks(Interlocked.Read(ref _connectedAtTicks));

    /// <summary>When any line was last received; null before the first.</summary>
    public DateTimeOffset? LastMessageAt => FromTicks(Interlocked.Read(ref _lastMessageTicks));

    public long Attempts(ConnectResult result) => Interlocked.Read(ref _attempts[(int)result]);

    public long Disconnects(DisconnectReason reason) => Interlocked.Read(ref _disconnects[(int)reason]);

    public long Messages(GpsdClass cls) => Interlocked.Read(ref _messages[(int)cls]);

    public long ParseErrors(ParseErrorReason reason) => Interlocked.Read(ref _parseErrors[(int)reason]);

    public long TotalAttempts => Enum.GetValues<ConnectResult>().Sum(Attempts);

    internal void Attempted(ConnectResult result) => Interlocked.Increment(ref _attempts[(int)result]);

    internal void Connected(DateTimeOffset at)
    {
        Interlocked.Exchange(ref _reconnectDelayTicks, 0);
        Interlocked.Exchange(ref _connectedAtTicks, at.UtcTicks);
    }

    internal void VersionReceived() => Volatile.Write(ref _up, 1);

    internal void Disconnected(DisconnectReason reason)
    {
        Volatile.Write(ref _up, 0);
        Interlocked.Exchange(ref _connectedAtTicks, long.MinValue);
        Interlocked.Increment(ref _disconnects[(int)reason]);
    }

    internal void Waiting(TimeSpan delay) => Interlocked.Exchange(ref _reconnectDelayTicks, delay.Ticks);

    internal void LineReceived(DateTimeOffset at) => Interlocked.Exchange(ref _lastMessageTicks, at.UtcTicks);

    internal void Message(GpsdClass cls) => Interlocked.Increment(ref _messages[(int)cls]);

    internal void ParseError(ParseErrorReason reason) => Interlocked.Increment(ref _parseErrors[(int)reason]);

    private static DateTimeOffset? FromTicks(long ticks) =>
        ticks == long.MinValue ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
}
