namespace GpsdExporter.Testing;

/// <summary>
/// Loads the gpsd session fixtures in <c>tests/fixtures</c>, which the test projects copy
/// to <c>fixtures/</c> next to the test assembly.
/// </summary>
/// <remarks>
/// A fixture is a gpsd session as received over the socket, one JSON object per line.
/// Lines starting with <c>#</c> are header comments and are stripped here, as are empty
/// lines; the exporter's parser has no special handling for either. Lines are returned as
/// raw bytes because some fixtures (notably <c>bad-input.jsonl</c>) contain invalid UTF-8
/// on purpose.
/// </remarks>
public static class Fixtures
{
    /// <summary>Every fixture in the spec's fixture catalogue, relative to the fixtures directory.</summary>
    public static IReadOnlyList<string> Catalogue { get; } =
    [
        "doc-examples.jsonl",
        "session-3d-fix.jsonl",
        "session-partial-epochs.jsonl",
        "session-fix-loss.jsonl",
        "session-sky-shrink.jsonl",
        "session-multi-gnss.jsonl",
        "session-two-devices.jsonl",
        "session-timing.jsonl",
        "bad-input.jsonl",
    ];

    public static string DirectoryPath { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>Every <c>gpsd-regress/*.jsonl</c> fixture, relative to the fixtures directory.</summary>
    public static IReadOnlyList<string> GpsdRegress() =>
        [.. Directory.EnumerateFiles(Path.Combine(DirectoryPath, "gpsd-regress"), "*.jsonl")
            .Select(f => Path.GetRelativePath(DirectoryPath, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>The catalogue plus every gpsd regression fixture.</summary>
    public static IReadOnlyList<string> All() => [.. Catalogue, .. GpsdRegress()];

    public static string PathOf(string name)
    {
        var path = Path.GetFullPath(Path.Combine(DirectoryPath, name));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture '{name}' not found in {DirectoryPath}.", path);
        }

        return path;
    }

    /// <summary>The fixture's lines in order, without comments, empty lines or line terminators.</summary>
    public static IReadOnlyList<byte[]> LoadBytes(string name)
    {
        var data = File.ReadAllBytes(PathOf(name));
        var lines = new List<byte[]>();
        var start = 0;
        while (start < data.Length)
        {
            var end = Array.IndexOf(data, (byte)'\n', start);
            if (end < 0)
            {
                end = data.Length;
            }

            var length = end - start;
            if (length > 0 && data[end - 1] == (byte)'\r')
            {
                length--;
            }

            if (length > 0 && data[start] != (byte)'#')
            {
                lines.Add(data.AsSpan(start, length).ToArray());
            }

            start = end + 1;
        }

        return lines;
    }

    /// <summary>
    /// The fixture's lines as strings. Invalid UTF-8 becomes U+FFFD, so use
    /// <see cref="LoadBytes"/> when the exact bytes matter.
    /// </summary>
    public static IReadOnlyList<string> Load(string name) =>
        [.. LoadBytes(name).Select(System.Text.Encoding.UTF8.GetString)];
}
