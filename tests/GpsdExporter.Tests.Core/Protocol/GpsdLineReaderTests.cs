using System.IO.Pipelines;
using System.Text;
using GpsdExporter.Protocol;
using GpsdExporter.Testing;

namespace GpsdExporter.Tests.Core.Protocol;

public sealed class GpsdLineReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Splits_lf_and_crlf_lines_and_strips_the_terminator()
    {
        var (lines, end) = await Read("{\"a\":1}\n{\"b\":2}\r\n{\"c\":3}\r\n");

        lines.ShouldBe(["{\"a\":1}", "{\"b\":2}", "{\"c\":3}"]);
        end.ShouldBe(GpsdStreamEnd.Closed);
    }

    [Fact]
    public async Task Skips_empty_lines_and_keeps_a_cr_that_is_not_part_of_the_terminator()
    {
        var (lines, _) = await Read("\n\r\na\rb\r\n\n");

        lines.ShouldBe(["a\rb"]);
    }

    [Fact]
    public async Task Discards_a_partial_line_when_the_stream_ends()
    {
        var (lines, end) = await Read("{\"class\":\"TPV\",\"mode\":1}\r\n{\"class\":\"TPV\",\"mo");

        lines.ShouldBe(["{\"class\":\"TPV\",\"mode\":1}"]);
        end.ShouldBe(GpsdStreamEnd.ClosedMidLine);
    }

    [Fact]
    public async Task A_stream_that_ends_after_a_cr_is_mid_line()
    {
        var (lines, end) = await Read("a\r\nb\r");

        lines.ShouldBe(["a"]);
        end.ShouldBe(GpsdStreamEnd.ClosedMidLine);
    }

    [Fact]
    public async Task Reassembles_lines_split_at_every_possible_byte_boundary()
    {
        var text = "{\"class\":\"VERSION\"}\r\n{\"class\":\"TPV\",\"mode\":3}\n{\"x\":\"é\"}\r\n";
        var bytes = Encoding.UTF8.GetBytes(text);

        for (var split = 1; split < bytes.Length; split++)
        {
            var (lines, end) = await Read([bytes[..split], bytes[split..]]);

            lines.ShouldBe(["{\"class\":\"VERSION\"}", "{\"class\":\"TPV\",\"mode\":3}", "{\"x\":\"é\"}"], $"split at {split}");
            end.ShouldBe(GpsdStreamEnd.Closed);
        }
    }

    [Fact]
    public async Task Reassembles_lines_delivered_one_byte_at_a_time()
    {
        var bytes = Encoding.UTF8.GetBytes("abc\r\ndef\nghi\r\n");

        var (lines, _) = await Read([.. bytes.Select(b => new[] { b })]);

        lines.ShouldBe(["abc", "def", "ghi"]);
    }

    [Fact]
    public async Task A_line_spanning_several_pipe_segments_is_delivered_whole()
    {
        // Small segments force the pipe to hold the line in many buffer segments.
        var line = new string('x', 10_000);
        var handler = new RecordingHandler();
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 512, useSynchronizationContext: false));

        var read = new GpsdLineReader().ReadAllAsync(pipe.Reader, handler, Ct);
        foreach (var chunk in Encoding.ASCII.GetBytes(line + "\n").Chunk(700))
        {
            await pipe.Writer.WriteAsync(chunk, Ct);
        }

        await pipe.Writer.CompleteAsync();
        await read;

        handler.Lines.ShouldBe([line]);
    }

    [Fact]
    public async Task A_line_of_exactly_the_limit_is_kept_and_one_byte_more_is_too_long()
    {
        var atLimit = new string('a', 100);
        var overLimit = new string('b', 101);

        var handler = await ReadWith(new GpsdLineReader(maxLineLength: 100), $"{atLimit}\r\n{overLimit}\r\nok\n");

        handler.Lines.ShouldBe([atLimit, "ok"]);
        handler.TooLong.ShouldBe([101]);
    }

    [Fact]
    public async Task A_line_at_the_limit_whose_lf_arrives_late_is_kept()
    {
        var atLimit = new string('a', 100);
        var handler = new RecordingHandler();
        var pipe = new Pipe();

        var read = new GpsdLineReader(maxLineLength: 100).ReadAllAsync(pipe.Reader, handler, Ct);
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes(atLimit + "\r"), Ct);
        await pipe.Writer.WriteAsync("\n"u8.ToArray(), Ct);
        await pipe.Writer.CompleteAsync();
        await read;

        handler.Lines.ShouldBe([atLimit]);
        handler.TooLong.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_too_long_line_arriving_in_pieces_is_discarded_and_the_next_line_still_read()
    {
        var chunks = new List<byte[]> { "{\"class\":\"TPV\",\"padding\":\""u8.ToArray() };
        chunks.AddRange(Enumerable.Repeat(Encoding.ASCII.GetBytes(new string('x', 4096)), 20));
        chunks.Add("\"}\r\n{\"class\":\"TPV\",\"mode\":3}\r\n"u8.ToArray());
        var expectedLength = chunks.Take(21).Sum(c => c.Length) + 2;

        var handler = new RecordingHandler();
        var pipe = new Pipe();
        var read = new GpsdLineReader(maxLineLength: 1024).ReadAllAsync(pipe.Reader, handler, Ct);
        foreach (var chunk in chunks)
        {
            await pipe.Writer.WriteAsync(chunk, Ct);
        }

        await pipe.Writer.CompleteAsync();
        (await read).ShouldBe(GpsdStreamEnd.Closed);

        handler.TooLong.ShouldBe([expectedLength]);
        handler.Lines.ShouldBe(["{\"class\":\"TPV\",\"mode\":3}"]);
    }

    [Fact]
    public async Task A_too_long_line_is_not_buffered_whole()
    {
        // The writer pauses once 128 KiB is unread. A reader that buffered the whole line
        // would never let a 4 MiB line through, and the write would hang.
        var handler = new RecordingHandler();
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 128 * 1024, resumeWriterThreshold: 64 * 1024, useSynchronizationContext: false));
        var read = new GpsdLineReader().ReadAllAsync(pipe.Reader, handler, Ct);

        var chunk = Encoding.ASCII.GetBytes(new string('x', 16 * 1024));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 256; i++)
        {
            await pipe.Writer.WriteAsync(chunk, timeout.Token);
        }

        await pipe.Writer.WriteAsync("\nnext\n"u8.ToArray(), timeout.Token);
        await pipe.Writer.CompleteAsync();
        await read;

        handler.TooLong.ShouldBe([4L * 1024 * 1024]);
        handler.Lines.ShouldBe(["next"]);
    }

    [Fact]
    public async Task A_too_long_line_cut_off_by_the_end_of_the_stream_is_reported_as_too_long()
    {
        var handler = await ReadWith(new GpsdLineReader(maxLineLength: 10), "ok\n" + new string('x', 50));

        handler.Lines.ShouldBe(["ok"]);
        handler.TooLong.ShouldBe([50]);
    }

    [Fact]
    public async Task The_default_limit_is_64_kib_and_bad_input_fixture_has_one_line_over_it()
    {
        new GpsdLineReader().MaxLineLength.ShouldBe(64 * 1024);

        var bytes = Fixtures.LoadBytes("bad-input.jsonl").SelectMany(l => l.Concat("\r\n"u8.ToArray())).ToArray();
        var handler = await ReadWith(new GpsdLineReader(), bytes);

        handler.TooLong.Count.ShouldBe(1);
        handler.Lines.Count.ShouldBe(Fixtures.LoadBytes("bad-input.jsonl").Count - 1);
        handler.Lines[^1].ShouldStartWith("{\"class\":\"TPV\"");
    }

    [Fact]
    public async Task Invalid_utf8_is_passed_through_as_bytes()
    {
        var handler = await ReadWith(new GpsdLineReader(), [0x7B, 0xFF, 0xFE, 0x7D, 0x0A]);

        handler.RawLines.ShouldHaveSingleItem().ShouldBe(new byte[] { 0x7B, 0xFF, 0xFE, 0x7D });
    }

    [Fact]
    public async Task Cancellation_stops_reading_and_completes_the_reader()
    {
        var pipe = new Pipe();
        using var cts = new CancellationTokenSource();
        var read = new GpsdLineReader().ReadAllAsync(pipe.Reader, new RecordingHandler(), cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => read);
        await Should.ThrowAsync<OperationCanceledException>(async () => await pipe.Writer.WriteAsync(new byte[1], Ct));
    }

    [Fact]
    public void The_limit_must_be_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new GpsdLineReader(0));
    }

    private static Task<(IReadOnlyList<string> Lines, GpsdStreamEnd End)> Read(string text) =>
        Read([Encoding.UTF8.GetBytes(text)]);

    private static async Task<(IReadOnlyList<string> Lines, GpsdStreamEnd End)> Read(IEnumerable<byte[]> chunks)
    {
        var handler = new RecordingHandler();
        var end = await Run(new GpsdLineReader(), handler, chunks);
        return (handler.Lines, end);
    }

    private static Task<RecordingHandler> ReadWith(GpsdLineReader reader, string text) =>
        ReadWith(reader, Encoding.UTF8.GetBytes(text));

    private static async Task<RecordingHandler> ReadWith(GpsdLineReader reader, byte[] bytes)
    {
        var handler = new RecordingHandler();
        await Run(reader, handler, [bytes]);
        return handler;
    }

    private static async Task<GpsdStreamEnd> Run(GpsdLineReader reader, RecordingHandler handler, IEnumerable<byte[]> chunks)
    {
        var pipe = new Pipe();
        var read = reader.ReadAllAsync(pipe.Reader, handler, Ct);
        foreach (var chunk in chunks)
        {
            await pipe.Writer.WriteAsync(chunk, Ct);
        }

        await pipe.Writer.CompleteAsync();
        return await read;
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
