using System.Globalization;
using System.Net;
using System.Net.Sockets;
using GpsdExporter.Testing;

// A stand-in gpsd for local development. Every client gets the fixture's preamble (VERSION,
// DEVICES, WATCH) once, then its epochs over and over, one epoch a second, so the exporter
// always has live data. It ignores what clients send, including ?WATCH. It runs until it is
// killed (Ctrl+C, or the AppHost stopping).
//
// FAKE_GPSD_PORT     the TCP port to listen on (default 2947, gpsd's own). The AppHost sets it.
// FAKE_GPSD_FIXTURE  a fixture from tests/fixtures (default session-3d-fix.jsonl).

var port = int.Parse(Environment.GetEnvironmentVariable("FAKE_GPSD_PORT") ?? "2947", CultureInfo.InvariantCulture);
var fixture = Environment.GetEnvironmentVariable("FAKE_GPSD_FIXTURE") ?? "session-3d-fix.jsonl";

var tpv = "\"class\":\"TPV\""u8.ToArray();
var sky = "\"class\":\"SKY\""u8.ToArray();

// gpsd terminates every line with CR-LF.
var lines = Fixtures.LoadBytes(fixture).Select(l => (byte[])[.. l, (byte)'\r', (byte)'\n']).ToArray();
var preamble = lines.TakeWhile(l => !Contains(l, tpv) && !Contains(l, sky)).ToArray();
var epochs = lines.Skip(preamble.Length).ToArray();
if (epochs.Length == 0)
{
    throw new InvalidOperationException($"Fixture '{fixture}' has no TPV or SKY lines to replay.");
}

// "localhost" may resolve to either loopback address, so listen on both where there is IPv6.
var listeners = new List<TcpListener> { Listen(IPAddress.Loopback, port) };
try
{
    listeners.Add(Listen(IPAddress.IPv6Loopback, port));
}
catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressFamilyNotSupported or SocketError.AddressNotAvailable)
{
}

Console.WriteLine($"fake gpsd: replaying {fixture} on port {port}");
await Task.WhenAll(listeners.Select(AcceptLoopAsync)).ConfigureAwait(false);

async Task AcceptLoopAsync(TcpListener listener)
{
    while (true)
    {
        var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
        _ = ServeAsync(socket);
    }
}

async Task ServeAsync(Socket socket)
{
    var client = socket.RemoteEndPoint;
    Console.WriteLine($"fake gpsd: {client} connected");
    try
    {
        using (socket)
        {
            socket.NoDelay = true;
            foreach (var line in preamble)
            {
                await socket.SendAsync(line).ConfigureAwait(false);
            }

            var first = true;
            while (true)
            {
                foreach (var line in epochs)
                {
                    // gpsd reports once a second, and a TPV starts each epoch.
                    if (Contains(line, tpv) && !first)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    }

                    first = false;
                    await socket.SendAsync(line).ConfigureAwait(false);
                }
            }
        }
    }
    catch (SocketException)
    {
        Console.WriteLine($"fake gpsd: {client} disconnected");
    }
}

static TcpListener Listen(IPAddress address, int port)
{
    var listener = new TcpListener(address, port);
    listener.Start();
    return listener;
}

static bool Contains(byte[] line, byte[] value) => line.AsSpan().IndexOf(value) >= 0;
