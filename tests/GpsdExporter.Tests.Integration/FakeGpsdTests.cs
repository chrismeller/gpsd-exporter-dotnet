using System.Net.Sockets;
using System.Text;
using GpsdExporter.Testing;

namespace GpsdExporter.Tests.Integration;

/// <summary>Self-tests for <see cref="FakeGpsd"/> (EXT-884), driven by a plain TCP client.</summary>
public sealed class FakeGpsdTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Refuses_connections_until_started()
    {
        await using var gpsd = new FakeGpsd();

        var refused = await Should.ThrowAsync<SocketException>(() => Connect(gpsd));
        refused.SocketErrorCode.ShouldBe(SocketError.ConnectionRefused);

        gpsd.Start();
        using var client = await Connect(gpsd);
        await gpsd.AcceptAsync(Ct);

        gpsd.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sends_version_on_connect_then_scripted_lines()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        using var client = await Connect(gpsd);
        var reader = new LineReader(client);

        var connection = await gpsd.AcceptAsync(Ct);
        await connection.SendLinesAsync(["{\"class\":\"DEVICES\",\"devices\":[]}", "{\"class\":\"WATCH\",\"enable\":true}"], Ct);

        (await reader.ReadLineAsync()).ShouldBe(FakeGpsd.DefaultVersionLine + "\r\n");
        (await reader.ReadLineAsync()).ShouldBe("{\"class\":\"DEVICES\",\"devices\":[]}\r\n");
        (await reader.ReadLineAsync()).ShouldBe("{\"class\":\"WATCH\",\"enable\":true}\r\n");
    }

    [Fact]
    public async Task Sends_a_fixture_byte_for_byte_with_crlf_and_without_comments()
    {
        await using var gpsd = FakeGpsd.CreateStarted(new FakeGpsdOptions { SendVersionOnConnect = false });
        using var client = await Connect(gpsd);
        var reader = new LineReader(client);

        var connection = await gpsd.AcceptAsync(Ct);
        await connection.SendFixtureAsync("bad-input.jsonl", Ct);
        await connection.CloseAsync();

        var expected = Fixtures.LoadBytes("bad-input.jsonl").SelectMany(l => l.Concat("\r\n"u8.ToArray())).ToArray();
        (await reader.ReadToEndAsync()).ShouldBe(expected);
    }

    [Fact]
    public async Task Records_every_line_the_client_sends()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        using var client = await Connect(gpsd);
        var connection = await gpsd.AcceptAsync(Ct);

        await Send(client, "?WATCH={\"enable\":true,\"json\":true,\"pps\":true};\n");
        await Send(client, "?VERSION;\r\n");
        await connection.WaitForLineAsync("?VERSION;", Ct);

        connection.ReceivedLines.ShouldBe(["?WATCH={\"enable\":true,\"json\":true,\"pps\":true};", "?VERSION;"]);
    }

    [Fact]
    public async Task Waiting_for_a_line_sees_fragments_completed_later()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        using var client = await Connect(gpsd);
        var connection = await gpsd.AcceptAsync(Ct);

        var wait = connection.WaitForLineAsync(l => l.StartsWith("?WATCH", StringComparison.Ordinal), Ct);
        await Send(client, "?WAT");
        await Send(client, "CH={\"enable\":false};\n");

        (await wait).ShouldBe("?WATCH={\"enable\":false};");
    }

    [Fact]
    public async Task Stalls_when_told_to_send_nothing()
    {
        await using var gpsd = FakeGpsd.CreateStarted(new FakeGpsdOptions { SendVersionOnConnect = false });
        using var client = await Connect(gpsd);
        var connection = await gpsd.AcceptAsync(Ct);

        // A round trip proves the server has processed the connection; it still sent nothing.
        await Send(client, "?VERSION;\n");
        await connection.WaitForLineAsync("?VERSION;", Ct);

        client.Available.ShouldBe(0);
        connection.IsClosed.ShouldBeFalse();
    }

    [Fact]
    public async Task Closing_gracefully_gives_the_client_end_of_stream()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        using var client = await Connect(gpsd);
        var reader = new LineReader(client);
        var connection = await gpsd.AcceptAsync(Ct);

        await connection.CloseAsync();

        Encoding.ASCII.GetString(await reader.ReadToEndAsync()).ShouldBe(FakeGpsd.DefaultVersionLine + "\r\n");
    }

    [Fact]
    public async Task Aborting_resets_the_connection()
    {
        await using var gpsd = FakeGpsd.CreateStarted(new FakeGpsdOptions { SendVersionOnConnect = false });
        using var client = await Connect(gpsd);
        var connection = await gpsd.AcceptAsync(Ct);

        connection.Abort();

        var ex = await Should.ThrowAsync<SocketException>(async () =>
        {
            var buffer = new byte[16];
            await client.Client.ReceiveAsync(buffer, SocketFlags.None, Ct);
        });
        ex.SocketErrorCode.ShouldBe(SocketError.ConnectionReset);
    }

    [Fact]
    public async Task Can_close_half_way_through_a_line()
    {
        await using var gpsd = FakeGpsd.CreateStarted(new FakeGpsdOptions { SendVersionOnConnect = false });
        using var client = await Connect(gpsd);
        var reader = new LineReader(client);
        var connection = await gpsd.AcceptAsync(Ct);

        await connection.SendLineAsync("{\"class\":\"TPV\",\"mode\":1}", Ct);
        await connection.SendPartialLineAndCloseAsync("{\"class\":\"TPV\",\"mode\":3,\"lat\":51.4}", 23, Ct);

        Encoding.ASCII.GetString(await reader.ReadToEndAsync())
            .ShouldBe("{\"class\":\"TPV\",\"mode\":1}\r\n{\"class\":\"TPV\",\"mode\":3");
    }

    [Fact]
    public async Task Restarts_on_the_same_port_and_drops_existing_clients()
    {
        await using var gpsd = FakeGpsd.CreateStarted(new FakeGpsdOptions { SendVersionOnConnect = false });
        var port = gpsd.Port;
        using var first = await Connect(gpsd);
        var firstConnection = await gpsd.AcceptAsync(Ct);

        await gpsd.StopAsync();

        firstConnection.IsClosed.ShouldBeTrue();
        (await Should.ThrowAsync<SocketException>(() => Connect(gpsd))).SocketErrorCode.ShouldBe(SocketError.ConnectionRefused);

        gpsd.Start();
        using var second = await Connect(gpsd);
        await gpsd.AcceptAsync(Ct);

        gpsd.Port.ShouldBe(port);
        gpsd.Connections.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Knows_when_the_client_disconnects()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        var client = await Connect(gpsd);
        var connection = await gpsd.AcceptAsync(Ct);

        client.Dispose();

        await connection.WaitForClientCloseAsync(Ct);
        connection.ClientClosed.IsCompleted.ShouldBeTrue();
    }

    private static async Task<TcpClient> Connect(FakeGpsd gpsd)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(gpsd.EndPoint, Ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task Send(TcpClient client, string text) =>
        await client.Client.SendAsync(Encoding.ASCII.GetBytes(text), SocketFlags.None, Ct);

    /// <summary>Reads CR-LF lines (terminator included) or everything until end of stream.</summary>
    private sealed class LineReader(TcpClient client)
    {
        private readonly List<byte> _buffer = [];

        public async Task<string> ReadLineAsync()
        {
            while (true)
            {
                var newline = _buffer.IndexOf((byte)'\n');
                if (newline >= 0)
                {
                    var line = _buffer.Take(newline + 1).ToArray();
                    _buffer.RemoveRange(0, newline + 1);
                    return Encoding.UTF8.GetString(line);
                }

                if (!await FillAsync())
                {
                    throw new EndOfStreamException();
                }
            }
        }

        public async Task<byte[]> ReadToEndAsync()
        {
            while (await FillAsync())
            {
            }

            return [.. _buffer];
        }

        private async Task<bool> FillAsync()
        {
            var chunk = new byte[8192];
            var read = await client.Client.ReceiveAsync(chunk, SocketFlags.None, Ct);
            _buffer.AddRange(chunk.AsSpan(0, read));
            return read > 0;
        }
    }
}
