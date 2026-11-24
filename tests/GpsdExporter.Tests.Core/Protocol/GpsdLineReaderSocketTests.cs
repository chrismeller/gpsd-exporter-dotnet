using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using GpsdExporter.Protocol;
using GpsdExporter.Testing;

namespace GpsdExporter.Tests.Core.Protocol;

/// <summary>
/// The line reader on a real socket from FakeGpsd. The end-to-end versions of these
/// tests, with counters and state, are in the connection manager tests.
/// </summary>
public sealed class GpsdLineReaderSocketTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task a_70_kib_line_is_dropped_and_the_following_tpv_is_read()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        using var client = new TcpClient();
        await client.ConnectAsync(gpsd.EndPoint, Ct);
        var connection = await gpsd.AcceptAsync(Ct);

        var handler = new RecordingHandler();
        var read = new GpsdLineReader().ReadAllAsync(PipeReader.Create(client.GetStream()), handler, Ct);

        var longLine = "{\"class\":\"TPV\",\"padding\":\"" + new string('x', 70 * 1024) + "\"}";
        await connection.SendLineAsync(longLine, Ct);
        await connection.SendLineAsync("{\"class\":\"TPV\",\"device\":\"/dev/ttyACM0\",\"mode\":3}", Ct);
        await connection.CloseAsync();

        (await read).ShouldBe(GpsdStreamEnd.Closed);
        handler.TooLong.ShouldBe([Encoding.ASCII.GetByteCount(longLine)]);
        handler.Lines.ShouldBe([FakeGpsd.DefaultVersionLine, "{\"class\":\"TPV\",\"device\":\"/dev/ttyACM0\",\"mode\":3}"]);
    }

    [Fact]
    public async Task a_line_cut_off_by_gpsd_closing_is_discarded()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        using var client = new TcpClient();
        await client.ConnectAsync(gpsd.EndPoint, Ct);
        var connection = await gpsd.AcceptAsync(Ct);

        var handler = new RecordingHandler();
        var read = new GpsdLineReader().ReadAllAsync(PipeReader.Create(client.GetStream()), handler, Ct);

        await connection.SendPartialLineAndCloseAsync("{\"class\":\"TPV\",\"mode\":3,\"lat\":51.4}", 23, Ct);

        (await read).ShouldBe(GpsdStreamEnd.ClosedMidLine);
        handler.Lines.ShouldBe([FakeGpsd.DefaultVersionLine]);
        handler.TooLong.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_fixture_replays_line_for_line()
    {
        foreach (var fixture in Fixtures.All().Where(f => f != "bad-input.jsonl"))
        {
            await using var gpsd = FakeGpsd.CreateStarted(new FakeGpsdOptions { SendVersionOnConnect = false });
            using var client = new TcpClient();
            await client.ConnectAsync(gpsd.EndPoint, Ct);
            var connection = await gpsd.AcceptAsync(Ct);

            var handler = new RecordingHandler();
            var read = new GpsdLineReader().ReadAllAsync(PipeReader.Create(client.GetStream()), handler, Ct);
            await connection.SendFixtureAsync(fixture, Ct);
            await connection.CloseAsync();
            await read;

            handler.RawLines.ShouldBe(Fixtures.LoadBytes(fixture), fixture);
        }
    }

    private sealed class RecordingHandler : IGpsdLineHandler
    {
        public List<byte[]> RawLines { get; } = [];

        public List<string> Lines => [.. RawLines.Select(Encoding.UTF8.GetString)];

        public List<long> TooLong { get; } = [];

        public void OnLine(ReadOnlySpan<byte> line) => RawLines.Add(line.ToArray());

        public void OnLineTooLong(long length) => TooLong.Add(length);
    }
}
