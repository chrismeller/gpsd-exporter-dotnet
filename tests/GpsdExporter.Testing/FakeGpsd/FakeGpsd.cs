using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace GpsdExporter.Testing;

/// <summary>
/// A scripted stand-in for gpsd, for integration tests. It is a plain <see cref="TcpListener"/>
/// on the loopback interface: gpsd speaks newline-terminated JSON over raw TCP, not HTTP.
/// </summary>
/// <remarks>
/// <para>
/// A new instance reserves a free port but does not listen, so connections are refused until
/// <see cref="Start"/> is called. <see cref="StopAsync"/> and <see cref="RestartAsync"/> stop
/// and start listening on the same port.
/// </para>
/// <para>
/// Each accepted client becomes a <see cref="FakeGpsdConnection"/>, which the test drives:
/// send lines or a fixture, read what the client sent, stall, or close. Nothing here uses a
/// timer. Every wait completes on a socket event, so the client under test can run on a
/// <c>FakeTimeProvider</c>; <see cref="DefaultTimeout"/> only stops a broken test from hanging.
/// </para>
/// </remarks>
public sealed class FakeGpsd : IAsyncDisposable
{
    /// <summary>The VERSION object gpsd 3.22 sends as soon as a client connects.</summary>
    public const string DefaultVersionLine =
        "{\"class\":\"VERSION\",\"release\":\"3.22\",\"rev\":\"3.22\",\"proto_major\":3,\"proto_minor\":14}";

    /// <summary>Safety net for waits, in real time. Tests should never get near it.</summary>
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly List<FakeGpsdConnection> _connections = [];
    private Channel<FakeGpsdConnection> _accepted = Channel.CreateUnbounded<FakeGpsdConnection>();
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private CancellationTokenSource? _stopping;

    public FakeGpsd(FakeGpsdOptions? options = null)
    {
        Options = options ?? new FakeGpsdOptions();
        Port = ReserveFreePort();
    }

    public FakeGpsdOptions Options { get; }

    /// <summary>The loopback port, fixed for the lifetime of this instance.</summary>
    public int Port { get; }

    public IPEndPoint EndPoint => new(IPAddress.Loopback, Port);

    /// <summary><c>127.0.0.1:port</c>, in the form the exporter's <c>-d</c> flag takes.</summary>
    public string Address => $"127.0.0.1:{Port}";

    public bool IsListening { get; private set; }

    /// <summary>Every connection accepted so far, across restarts, oldest first.</summary>
    public IReadOnlyList<FakeGpsdConnection> Connections
    {
        get
        {
            lock (_gate)
            {
                return [.. _connections];
            }
        }
    }

    /// <summary>Creates an instance that is already listening.</summary>
    public static FakeGpsd CreateStarted(FakeGpsdOptions? options = null)
    {
        var gpsd = new FakeGpsd(options);
        gpsd.Start();
        return gpsd;
    }

    /// <summary>Starts listening on <see cref="Port"/>.</summary>
    public void Start()
    {
        if (IsListening)
        {
            throw new InvalidOperationException("FakeGpsd is already listening.");
        }

        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();

        _listener = listener;
        _stopping = new CancellationTokenSource();
        _accepted = Channel.CreateUnbounded<FakeGpsdConnection>();
        IsListening = true;
        _acceptLoop = AcceptLoopAsync(listener, _accepted.Writer, _stopping.Token);
    }

    /// <summary>Stops listening and aborts every open connection, as if gpsd had exited.</summary>
    public async Task StopAsync()
    {
        if (!IsListening)
        {
            return;
        }

        IsListening = false;
        await _stopping!.CancelAsync().ConfigureAwait(false);
        _listener!.Stop();
        await _acceptLoop!.ConfigureAwait(false);
        _stopping.Dispose();

        foreach (var connection in Connections)
        {
            connection.Abort();
        }
    }

    /// <summary>Stops, then starts listening again on the same port.</summary>
    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Start();
    }

    /// <summary>
    /// Waits for the next client to connect. By then the VERSION line (if
    /// <see cref="FakeGpsdOptions.SendVersionOnConnect"/>) has been sent.
    /// </summary>
    public async Task<FakeGpsdConnection> AcceptAsync(CancellationToken cancellationToken = default)
    {
        if (!IsListening)
        {
            throw new InvalidOperationException("FakeGpsd is not listening; call Start first.");
        }

        using var timeout = Timeout(cancellationToken);
        return await _accepted.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        foreach (var connection in Connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static CancellationTokenSource Timeout(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DefaultTimeout);
        return cts;
    }

    private async Task AcceptLoopAsync(TcpListener listener, ChannelWriter<FakeGpsdConnection> accepted, CancellationToken stopping)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var socket = await listener.AcceptSocketAsync(stopping).ConfigureAwait(false);
                var connection = new FakeGpsdConnection(socket);
                lock (_gate)
                {
                    _connections.Add(connection);
                }

                if (Options.SendVersionOnConnect)
                {
                    await connection.SendLineAsync(Options.VersionLine, stopping).ConfigureAwait(false);
                }

                await accepted.WriteAsync(connection, stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException) when (stopping.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (stopping.IsCancellationRequested)
        {
        }
        finally
        {
            accepted.TryComplete();
        }
    }

    private static int ReserveFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }
}
