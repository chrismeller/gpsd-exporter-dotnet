using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GpsdExporter.Connection;

/// <summary>
/// The long-running loop that connects to gpsd, streams its reports into the
/// <see cref="StateStore"/>, and reconnects when the connection is lost (spec R1).
/// </summary>
/// <remarks>
/// <para>
/// One connection and one reader at a time (AC1.7): the loop connects, runs the session to its
/// end, then waits out the backoff delay before the next attempt. No connection error ends the
/// loop (AC1.6); only the host stopping does.
/// </para>
/// <para>
/// Every timer (connect timeout, backoff, keepalive) uses the injected <see cref="TimeProvider"/>.
/// </para>
/// </remarks>
internal sealed class GpsdConnectionManager : BackgroundService
{
    private static readonly TimeSpan UnwatchTimeout = TimeSpan.FromSeconds(1);

    private readonly GpsdOptions _options;
    private readonly StateStore _store;
    private readonly ConnectionStats _stats;
    private readonly TimeProvider _time;
    private readonly ILogger<GpsdConnectionManager> _logger;
    private readonly ReconnectBackoff _backoff;
    private readonly GpsdLineReader _lineReader = new();
    private readonly BadLineLog _badLines;
    private readonly string _host;
    private readonly int _port;
    private readonly byte[] _watch;
    private int _protocolWarned;

    public GpsdConnectionManager(
        GpsdOptions options,
        StateStore store,
        ConnectionStats stats,
        TimeProvider time,
        ILogger<GpsdConnectionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        }

        _options = options;
        _store = store;
        _stats = stats;
        _time = time;
        _logger = logger;
        _badLines = new BadLineLog(logger, time);
        _backoff = new ReconnectBackoff(options.ReconnectInitialDelay, options.ReconnectMaxDelay);
        if (!GpsdOptions.TryParseAddress(options.Address, out _host, out _port))
        {
            throw new ArgumentException($"Invalid gpsd address '{options.Address}'.", nameof(options));
        }

        _watch = Encoding.ASCII.GetBytes(options.WatchCommand + "\n");
    }

    public override void Dispose()
    {
        _badLines.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var versionSeen = await ConnectAndRunAsync(stoppingToken).ConfigureAwait(false);
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (versionSeen)
            {
                _backoff.Reset();
            }

            var delay = _backoff.Next();
            _stats.Waiting(delay);
            Log.ReconnectDelay(_logger, _options.Address, delay);
            try
            {
                await Task.Delay(delay, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One connection attempt and, if it succeeds, the session. Returns whether VERSION arrived.</summary>
    private async Task<bool> ConnectAndRunAsync(CancellationToken stoppingToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            var result = await ConnectAsync(socket, stoppingToken).ConfigureAwait(false);
            if (result is null)
            {
                return false;
            }

            _stats.Attempted(result.Value);
            if (result != ConnectResult.Success)
            {
                return false;
            }

            var stream = new NetworkStream(socket, ownsSocket: true);
            await using (stream.ConfigureAwait(false))
            {
                return await RunSessionAsync(stream, stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            socket.Dispose();
        }
    }

    /// <summary>Connects with the configured timeout. Null means the host is stopping.</summary>
    private async Task<ConnectResult?> ConnectAsync(Socket socket, CancellationToken stoppingToken)
    {
        using var timeout = new CancellationTokenSource(_options.ConnectTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
        try
        {
            await socket.ConnectAsync(new DnsEndPoint(_host, _port), linked.Token).ConfigureAwait(false);
            return ConnectResult.Success;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException ex)
        {
            Log.ConnectFailed(_logger, _options.Address, ConnectResult.Timeout.Label(), ex);
            return ConnectResult.Timeout;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            Log.ConnectFailed(_logger, _options.Address, ConnectResult.Refused.Label(), ex);
            return ConnectResult.Refused;
        }
        catch (SocketException ex)
        {
            Log.ConnectFailed(_logger, _options.Address, ConnectResult.Error.Label(), ex);
            return ConnectResult.Error;
        }
    }

    private async Task<bool> RunSessionAsync(NetworkStream stream, CancellationToken stoppingToken)
    {
        var session = new Session(this, _time.GetUtcNow());
        _stats.Connected(session.ConnectedAt);
        Log.Connected(_logger, _options.Address);

        using var sessionEnd = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var reason = DisconnectReason.Error;
        Exception? error = null;
        Task<GpsdStreamEnd>? reading = null;
        Task<bool>? keepalive = null;
        try
        {
            await stream.WriteAsync(_watch, stoppingToken).ConfigureAwait(false);

            var pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
            reading = _lineReader.ReadAllAsync(pipe, session, sessionEnd.Token);
            keepalive = KeepaliveAsync(stream, session, sessionEnd.Token);

            var first = await Task.WhenAny(reading, keepalive).ConfigureAwait(false);
            if (first == reading)
            {
                await reading.ConfigureAwait(false);
                reason = DisconnectReason.Closed;
            }
            else if (await keepalive.ConfigureAwait(false))
            {
                reason = DisconnectReason.KeepaliveTimeout;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            reason = DisconnectReason.Shutdown;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            reason = DisconnectReason.Error;
            error = ex;
        }
        finally
        {
            await sessionEnd.CancelAsync().ConfigureAwait(false);
            await Settle(reading).ConfigureAwait(false);
            await Settle(keepalive).ConfigureAwait(false);
        }

        if (reason == DisconnectReason.Shutdown)
        {
            await UnwatchAsync(stream).ConfigureAwait(false);
        }

        // AC1.5: everything gpsd told us goes with the connection.
        _store.Reset();
        _stats.Disconnected(reason);
        Log.Disconnected(_logger, reason.Label(), error);
        return session.VersionSeen;
    }

    /// <summary>
    /// AC1.4: after <see cref="GpsdOptions.KeepaliveIdle"/> with nothing received, sends
    /// <c>?VERSION;</c>; returns true if nothing then arrives within
    /// <see cref="GpsdOptions.KeepaliveTimeout"/>.
    /// </summary>
    private async Task<bool> KeepaliveAsync(NetworkStream stream, Session session, CancellationToken cancellationToken)
    {
        var probe = Encoding.ASCII.GetBytes(GpsdOptions.VersionCommand + "\n");
        while (true)
        {
            var seen = session.Activity;
            var idle = _time.GetUtcNow() - session.LastActivityAt;
            var wait = _options.KeepaliveIdle - idle;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Log.KeepaliveProbe(_logger, idle);
            await stream.WriteAsync(probe, cancellationToken).ConfigureAwait(false);
            await Task.Delay(_options.KeepaliveTimeout, _time, cancellationToken).ConfigureAwait(false);
            if (session.Activity == seen)
            {
                return true;
            }
        }
    }

    /// <summary>AC1.8: tells gpsd to stop streaming before the socket closes. Best effort.</summary>
    private static async Task UnwatchAsync(NetworkStream stream)
    {
        try
        {
            using var timeout = new CancellationTokenSource(UnwatchTimeout);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(GpsdOptions.UnwatchCommand + "\n"), timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The connection is going anyway.
        }
    }

    private static async Task Settle(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // Already accounted for by the session's outcome.
        }
    }

    private void OnVersion(VersionReport version)
    {
        _stats.VersionReceived();
        Log.VersionReceived(_logger, version.Release, version.ProtoMajor, version.ProtoMinor);
        if (version.ProtoMajor != 3 && Interlocked.Exchange(ref _protocolWarned, 1) == 0)
        {
            Log.UnsupportedProtocol(_logger, version.ProtoMajor, version.ProtoMinor);
        }
    }

    /// <summary>One connection's line handler. Runs on the reader; the keepalive reads its activity.</summary>
    private sealed class Session(GpsdConnectionManager owner, DateTimeOffset connectedAt) : IGpsdLineHandler
    {
        private long _activity;
        private long _lastActivityTicks = connectedAt.UtcTicks;

        public DateTimeOffset ConnectedAt { get; } = connectedAt;

        public bool VersionSeen { get; private set; }

        /// <summary>Counts lines received, including discarded ones.</summary>
        public long Activity => Interlocked.Read(ref _activity);

        public DateTimeOffset LastActivityAt => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

        public void OnLine(ReadOnlySpan<byte> line)
        {
            var now = Touch();
            owner._stats.LineReceived(now);

            var result = GpsdParser.Parse(line);
            if (result.Class is { } cls)
            {
                owner._stats.Message(cls);
            }

            if (result.Error is { } error)
            {
                owner._stats.ParseError(error);
                owner._badLines.Discarded(error, line);
            }

            foreach (var fieldError in result.FieldErrors)
            {
                owner._stats.ParseError(fieldError.Reason);
                owner._badLines.Discarded(fieldError.Reason, line);
            }

            if (result.Report is not { } report)
            {
                return;
            }

            switch (report)
            {
                case VersionReport version:
                    VersionSeen = true;
                    owner.OnVersion(version);
                    break;
                case ErrorReport gpsdError:
                    Log.GpsdError(owner._logger, gpsdError.Message);
                    break;
            }

            var before = owner._store.Current.Devices;
            if (owner._store.Apply(report) is { } skipped)
            {
                owner._stats.ParseError(skipped);
                owner._badLines.Discarded(skipped, line);
            }

            var after = owner._store.Current.Devices;
            if (!ReferenceEquals(before, after))
            {
                LogDeviceChanges(before.Keys, after.Keys);
            }
        }

        public void OnLineTooLong(long length)
        {
            var now = Touch();
            owner._stats.LineReceived(now);
            owner._stats.ParseError(ParseErrorReason.TooLong);
            owner._badLines.DiscardedTooLong(length);
        }

        private DateTimeOffset Touch()
        {
            var now = owner._time.GetUtcNow();
            Interlocked.Exchange(ref _lastActivityTicks, now.UtcTicks);
            Interlocked.Increment(ref _activity);
            return now;
        }

        private void LogDeviceChanges(IEnumerable<string> before, IEnumerable<string> after)
        {
            var old = before.ToHashSet(StringComparer.Ordinal);
            foreach (var device in after)
            {
                if (!old.Remove(device))
                {
                    Log.DeviceAdded(owner._logger, device);
                }
            }

            foreach (var device in old)
            {
                Log.DeviceRemoved(owner._logger, device);
            }
        }
    }
}
