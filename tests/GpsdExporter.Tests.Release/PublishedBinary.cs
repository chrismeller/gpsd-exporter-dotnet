using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GpsdExporter.Tests.Release;

/// <summary>
/// The published binary named by <c>GPSD_EXPORTER_BINARY</c>. The release smoke tests run
/// only when it is set, in the binaries workflow; elsewhere they are skipped.
/// </summary>
internal static partial class PublishedBinary
{
    public const string Variable = "GPSD_EXPORTER_BINARY";

    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    /// <summary>The binary's path, or a skip when it is not configured.</summary>
    public static string Path
    {
        get
        {
            var path = Environment.GetEnvironmentVariable(Variable);
            Assert.SkipWhen(string.IsNullOrEmpty(path), $"{Variable} is not set; release smoke tests run only in the binaries workflow.");
            File.Exists(path).ShouldBeTrue($"{Variable} names {path}, which does not exist.");
            return path!;
        }
    }

    public static Process Start(params string[] args) => StartIn(workingDirectory: null, args);

    public static Process StartIn(string? workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo(Path)
        {
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        // Nothing from the runner's environment may change the settings, where it listens or OTLP.
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("GPSD_EXPORTER_", StringComparison.Ordinal) || k.StartsWith("DOTNET_", StringComparison.Ordinal) || k.StartsWith("ASPNETCORE_", StringComparison.Ordinal) || k.StartsWith("OTEL_", StringComparison.Ordinal)).ToList())
        {
            info.Environment.Remove(key);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("The binary did not start.");
    }

    [GeneratedRegex(@"Now listening on: (?<url>http://[^\s]+)")]
    public static partial Regex ListeningOn();
}

/// <summary>Collects a process's stdout and stderr as they arrive.</summary>
internal sealed class OutputCollector
{
    private readonly StringBuilder _text = new();
    private readonly Lock _lock = new();

    public OutputCollector(Process process)
    {
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public string Text
    {
        get
        {
            lock (_lock)
            {
                return _text.ToString();
            }
        }
    }

    public async Task<string> WaitForAsync(Regex pattern, TimeSpan limit, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            var line = Text.Split('\n').FirstOrDefault(pattern.IsMatch);
            if (line is not null)
            {
                return line;
            }

            await Task.Delay(20, cancellationToken);
        }

        throw new TimeoutException($"No output matched {pattern}. Output so far:\n{Text}");
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_lock)
        {
            _text.AppendLine(line);
        }
    }
}
