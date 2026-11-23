using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Integration;

/// <summary>The real executable, run as a process: command line, exit codes, start-up and HTTP limits (AC1.6, AC5.6, AC5.7, AC11.2).</summary>
public sealed partial class ProgramTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task T5_5_version_prints_the_version_and_exits_0()
    {
        var run = await RunToExitAsync("--version");

        run.ExitCode.ShouldBe(0);
        run.Stdout.ShouldStartWith($"gpsd-exporter {BuildInfo.Version} ");
        run.Stdout.ShouldContain(BuildInfo.Runtime);
    }

    [Fact]
    public async Task Help_prints_usage_and_exits_0()
    {
        var run = await RunToExitAsync("--help");

        run.ExitCode.ShouldBe(0);
        run.Stdout.ShouldContain("Usage: gpsd-exporter");
    }

    [Theory]
    [InlineData(new[] { "--bogus" }, "Unknown option '--bogus'.")]
    [InlineData(new[] { "-d" }, "Option '-d' needs a value.")]
    [InlineData(new[] { "-d", "[::1" }, "Gpsd:Address '[::1' is not host:port")]
    [InlineData(new[] { "-l", "example.com:9978" }, "Http:Listen 'example.com:9978' is not [addr]:port")]
    [InlineData(new[] { "--Staleness:Tpv=00:00:00" }, "Staleness:Tpv must be positive.")]
    [InlineData(new[] { "--Gpsd:ConnectTimeout=soon" }, "Gpsd:ConnectTimeout")]
    public async Task Invalid_command_line_or_configuration_exits_2_with_a_clear_message(string[] args, string message)
    {
        var run = await RunToExitAsync(args);

        run.ExitCode.ShouldBe(2);
        run.Stderr.ShouldContain(message);
        run.Stderr.ShouldStartWith("gpsd-exporter: ");
    }

    [Fact]
    public async Task Invalid_environment_variable_exits_2()
    {
        var bad = await RunToExitAsync(["-l", "127.0.0.1:0"], new Dictionary<string, string> { ["GPSD_EXPORTER_GPSD__ADDRESS"] = "[nope" });
        bad.ExitCode.ShouldBe(2);
        bad.Stderr.ShouldContain("Gpsd:Address '[nope' is not host:port");
    }

    [Fact]
    public async Task T11_2_Go_command_line_starts_connects_warns_about_p_and_serves_metrics()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        using var process = Start("-d", gpsd.Address, "-l", "127.0.0.1:0", "-v", "-p", "10s");
        var output = new OutputCollector(process);
        try
        {
            var connection = await gpsd.AcceptAsync(Ct);
            await connection.WaitForLineAsync("""?WATCH={"enable":true,"json":true,"pps":true};""", Ct);

            var listening = await output.WaitForAsync(ListeningOn(), Limit);
            var url = ListeningOn().Match(listening).Groups["url"].Value;
            using var http = new HttpClient();
            var deadline = DateTime.UtcNow + Limit;
            PrometheusText scrape;
            do
            {
                scrape = PrometheusText.Parse(await http.GetStringAsync(new Uri(new Uri(url), "/metrics"), Ct));
            }
            while (scrape.Value("gpsd_up") != 1 && DateTime.UtcNow < deadline && await Delay());

            scrape.Value("gpsd_up").ShouldBe(1);
            output.Text.ShouldContain("-p is ignored");
            output.Text.ShouldContain("dbug:"); // -v turned on Debug.
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AC1_6_a_port_that_cannot_be_bound_exits_1()
    {
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        var port = ((IPEndPoint)taken.LocalEndpoint).Port;

        var run = await RunToExitAsync("-l", $"127.0.0.1:{port}", "-d", "127.0.0.1:1");

        run.ExitCode.ShouldBe(1);
        run.Stderr.ShouldStartWith("gpsd-exporter: ");
        run.Stderr.ShouldContain(port.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task T5_6_a_client_sending_headers_slowly_is_closed_after_the_header_timeout()
    {
        using var process = Start("-l", "127.0.0.1:0", "-d", "127.0.0.1:1");
        var output = new OutputCollector(process);
        try
        {
            var listening = await output.WaitForAsync(ListeningOn(), Limit);
            var url = new Uri(ListeningOn().Match(listening).Groups["url"].Value);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, url.Port, Ct);
            var stream = client.GetStream();
            var started = Stopwatch.StartNew();
            await stream.WriteAsync("GET /metrics HTTP/1.1\r\nHost: localhost\r\n"u8.ToArray(), Ct);

            // Never finish the headers. Kestrel may answer 408 before it closes; either way
            // the stream ends.
            var buffer = new byte[4096];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(Limit);
            while (await stream.ReadAsync(buffer, timeout.Token) > 0)
            {
            }

            started.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(9));
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> Delay()
    {
        await Task.Delay(50, Ct);
        return true;
    }

    private static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "gpsd-exporter.exe" : "gpsd-exporter");

    private static Process Start(params string[] args) => Start(args, null);

    private static Process Start(string[] args, IReadOnlyDictionary<string, string>? environment)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
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

        // Nothing from the developer's or runner's environment should leak in.
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("GPSD_EXPORTER_", StringComparison.Ordinal)).ToList())
        {
            info.Environment.Remove(key);
        }

        info.Environment["ASPNETCORE_URLS"] = null;
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[key] = value;
        }

        return Process.Start(info) ?? throw new InvalidOperationException("The exporter did not start.");
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> RunToExitAsync(params string[] args) => RunToExitAsync(args, null);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunToExitAsync(string[] args, IReadOnlyDictionary<string, string>? environment)
    {
        using var process = Start(args, environment);
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Limit);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"gpsd-exporter {string.Join(' ', args)} did not exit.");
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    [GeneratedRegex(@"Now listening on: (?<url>http://[^\s]+)")]
    private static partial Regex ListeningOn();

    /// <summary>Collects stdout and stderr as they arrive.</summary>
    private sealed class OutputCollector
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

        public async Task<string> WaitForAsync(Regex pattern, TimeSpan limit)
        {
            var deadline = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < deadline)
            {
                var line = Text.Split('\n').FirstOrDefault(pattern.IsMatch);
                if (line is not null)
                {
                    return line;
                }

                await Task.Delay(20, Ct);
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
}
