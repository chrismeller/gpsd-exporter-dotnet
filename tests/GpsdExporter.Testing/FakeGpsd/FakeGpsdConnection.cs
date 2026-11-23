using System.Net.Sockets;
using System.Text;

namespace GpsdExporter.Testing;

/// <summary>
/// One client connection to <see cref="FakeGpsd"/>. Lines sent to the client are terminated
/// with CR-LF, as gpsd does. Everything the client sends is recorded.
/// </summary>
public sealed class FakeGpsdConnection : IAsyncDisposable
{
    private static readonly byte[] CrLf = "\r\n"u8.ToArray();

    private readonly Socket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Lock _gate = new();
    private readonly StringBuilder _received = new();
    private readonly List<string> _lines = [];
    private readonly TaskCompletionSource _clientClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _receiveLoop;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _closed;

    internal FakeGpsdConnection(Socket socket)
    {
        _socket = socket;
        _socket.NoDelay = true;
        _receiveLoop = ReceiveLoopAsync();
    }

    /// <summary>Everything the client has sent, decoded as UTF-8.</summary>
    public string ReceivedText
    {
        get
        {
            lock (_gate)
            {
                return _received.ToString();
            }
        }
    }

    /// <summary>
    /// Complete lines the client has sent, without their LF or CR-LF terminator. A trailing
    /// fragment with no newline yet is not included; see <see cref="ReceivedText"/>.
    /// </summary>
    public IReadOnlyList<string> ReceivedLines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    /// <summary>Completes when the client closes its side of the connection.</summary>
    public Task ClientClosed => _clientClosed.Task;

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default) =>
        SendLineAsync(Encoding.UTF8.GetBytes(line), cancellationToken);

    public Task SendLineAsync(ReadOnlyMemory<byte> line, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[line.Length + CrLf.Length];
        line.CopyTo(buffer);
        CrLf.CopyTo(buffer, line.Length);
        return SendRawAsync(buffer, cancellationToken);
    }

    public async Task SendLinesAsync(IEnumerable<string> lines, CancellationToken cancellationToken = default)
    {
        foreach (var line in lines)
        {
            await SendLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sends every line of a fixture (see <see cref="Fixtures"/>), byte for byte.</summary>
    public async Task SendFixtureAsync(string fixture, CancellationToken cancellationToken = default)
    {
        foreach (var line in Fixtures.LoadBytes(fixture))
        {
            await SendLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sends bytes exactly as given, with no terminator added.</summary>
    public async Task SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!bytes.IsEmpty)
            {
                var sent = await _socket.SendAsync(bytes, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                bytes = bytes[sent..];
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends the first <paramref name="byteCount"/> bytes of <paramref name="line"/> with no
    /// terminator, then closes the connection: gpsd going away half-way through a line.
    /// </summary>
    public async Task SendPartialLineAndCloseAsync(string line, int byteCount, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(byteCount, bytes.Length);

        await SendRawAsync(bytes.AsMemory(0, byteCount), cancellationToken).ConfigureAwait(false);
        await CloseAsync().ConfigureAwait(false);
    }

    /// <summary>Closes gracefully: the client reads end-of-stream (FIN).</summary>
    public async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }

        _socket.Close();
        await AwaitReceiveLoopAsync().ConfigureAwait(false);
    }

    /// <summary>Closes abruptly: the client sees a connection reset (RST).</summary>
    public void Abort()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        try
        {
            _socket.LingerState = new LingerOption(enable: true, seconds: 0);
        }
        catch (SocketException)
        {
        }

        _socket.Close();
    }

    /// <summary>
    /// Waits until the client has sent a complete line matching <paramref name="predicate"/>,
    /// and returns it. Lines received before the call count.
    /// </summary>
    public async Task<string> WaitForLineAsync(Func<string, bool> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        using var timeout = FakeGpsd.Timeout(cancellationToken);
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                foreach (var line in _lines)
                {
                    if (predicate(line))
                    {
                        return line;
                    }
                }

                changed = _changed.Task;
            }

            if (_clientClosed.Task.IsCompleted)
            {
                throw new InvalidOperationException(
                    $"The client closed the connection without sending a matching line. Received: {ReceivedText}");
            }

            await changed.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Waits for a line equal to <paramref name="expected"/>.</summary>
    public Task<string> WaitForLineAsync(string expected, CancellationToken cancellationToken = default) =>
        WaitForLineAsync(l => l == expected, cancellationToken);

    /// <summary>Waits until the client closes its side of the connection.</summary>
    public async Task WaitForClientCloseAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = FakeGpsd.Timeout(cancellationToken);
        await _clientClosed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Abort();
        await AwaitReceiveLoopAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    private async Task AwaitReceiveLoopAsync()
    {
        try
        {
            await _receiveLoop.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[4096];
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        var pending = new StringBuilder();
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await _socket.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                var count = decoder.GetChars(buffer, 0, read, chars, 0);
                lock (_gate)
                {
                    _received.Append(chars, 0, count);
                    for (var i = 0; i < count; i++)
                    {
                        if (chars[i] == '\n')
                        {
                            var line = pending.ToString();
                            _lines.Add(line.EndsWith('\r') ? line[..^1] : line);
                            pending.Clear();
                        }
                        else
                        {
                            pending.Append(chars[i]);
                        }
                    }

                    SignalChanged();
                }
            }
        }
        finally
        {
            _clientClosed.TrySetResult();
            lock (_gate)
            {
                SignalChanged();
            }
        }
    }

    private void SignalChanged()
    {
        var previous = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }
}
