using System.Text;
using GpsdExporter.Connection;
using GpsdExporter.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Core.Connection;

/// <summary>Bad lines at Debug, truncated, and a Warning summary at most once a minute.</summary>
public sealed class BadLineLogTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeLogger<GpsdConnectionManager> _logger = new();
    private readonly BadLineLog _log;

    public BadLineLogTests() => _log = new BadLineLog(_logger, _time);

    private IReadOnlyList<FakeLogRecord> Warnings => [.. _logger.Collector.GetSnapshot().Where(e => e.Level == LogLevel.Warning)];

    private IReadOnlyList<FakeLogRecord> Debugs => [.. _logger.Collector.GetSnapshot().Where(e => e.Level == LogLevel.Debug)];

    public void Dispose() => _log.Dispose();

    [Fact]
    public void Each_bad_line_is_logged_at_Debug_with_its_reason()
    {
        _log.Discarded(ParseErrorReason.InvalidJson, "{not json"u8);

        var entry = Debugs.ShouldHaveSingleItem();
        entry.Message.ShouldBe("Discarded a gpsd line (invalid_json): {not json");
    }

    [Fact]
    public void A_long_line_is_cut_to_200_characters_with_an_ellipsis()
    {
        var line = Encoding.UTF8.GetBytes(new string('x', 5000));

        _log.Discarded(ParseErrorReason.InvalidJson, line);

        var logged = Debugs.ShouldHaveSingleItem().Message["Discarded a gpsd line (invalid_json): ".Length..];
        logged.ShouldBe(new string('x', 200) + "…");
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(10, "aaaaaaaaaa")]
    [InlineData(200, null)]
    public void Excerpt_keeps_a_line_of_200_characters_or_fewer_whole(int length, string? expected)
    {
        var text = new string('a', length);

        BadLineLog.Excerpt(Encoding.UTF8.GetBytes(text)).ShouldBe(expected ?? text);
    }

    [Fact]
    public void Excerpt_counts_characters_not_bytes_and_never_splits_a_surrogate_pair()
    {
        // 199 ASCII characters, then an emoji (a surrogate pair) that would straddle the cut.
        var text = new string('a', 199) + "\U0001F6F0" + "tail";

        var excerpt = BadLineLog.Excerpt(Encoding.UTF8.GetBytes(text));

        excerpt.ShouldBe(new string('a', 199) + "…");

        var accented = new string('é', 250);
        BadLineLog.Excerpt(Encoding.UTF8.GetBytes(accented)).ShouldBe(new string('é', 200) + "…");
    }

    [Fact]
    public void The_first_bad_line_is_summarised_at_once()
    {
        _log.Discarded(ParseErrorReason.MissingClass, """{"x":1}"""u8);

        Warnings.ShouldHaveSingleItem().Message.ShouldBe("1 bad gpsd lines since 2026-11-23T10:00:00.000Z: missing_class=1. Debug logging shows each line.");
    }

    [Fact]
    public void Later_bad_lines_are_summarised_once_when_the_minute_ends()
    {
        _log.Discarded(ParseErrorReason.InvalidJson, "a"u8);
        _time.Advance(TimeSpan.FromSeconds(10));
        _log.Discarded(ParseErrorReason.InvalidJson, "b"u8);
        _log.Discarded(ParseErrorReason.BadField, "c"u8);
        _log.DiscardedTooLong(70_000);
        _time.Advance(TimeSpan.FromSeconds(49));

        Warnings.Count.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(1));

        Warnings.Count.ShouldBe(2);
        Warnings[1].Message.ShouldBe("3 bad gpsd lines since 2026-11-23T10:00:10.000Z: too_long=1, invalid_json=1, bad_field=1. Debug logging shows each line.");
    }

    [Fact]
    public void At_most_one_warning_a_minute_under_a_steady_stream()
    {
        for (var second = 0; second < 300; second++)
        {
            _log.Discarded(ParseErrorReason.InvalidJson, "x"u8);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        // One at once, then one at the end of each of the five minutes.
        Warnings.Count.ShouldBe(6);
        Debugs.Count.ShouldBe(300);
        Warnings.Sum(w => long.Parse(w.Message.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(300);
    }

    [Fact]
    public void After_a_quiet_minute_the_next_bad_line_is_summarised_at_once()
    {
        _log.Discarded(ParseErrorReason.InvalidJson, "a"u8);
        _time.Advance(TimeSpan.FromMinutes(5));
        Warnings.Count.ShouldBe(1);

        _log.Discarded(ParseErrorReason.NotObject, "[]"u8);

        Warnings.Count.ShouldBe(2);
        Warnings[1].Message.ShouldStartWith("1 bad gpsd lines since 2026-11-23T10:05:00.000Z: not_object=1.");
    }

    [Fact]
    public void A_line_that_is_too_long_logs_its_length_not_its_content()
    {
        _log.DiscardedTooLong(70_000);

        Debugs.ShouldHaveSingleItem().Message.ShouldBe("Discarded a gpsd line of 70000 bytes (too_long)");
    }
}
