using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;

namespace GpsdExporter.Tests.Host;

/// <summary>The exporter run as a process: what the command line prints, where it listens, and its exit codes.</summary>
public sealed partial class ProgramTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Version_prints_the_version_and_exits_0()
    {
        var run = await RunToExitAsync(["--version"]);

        run.ExitCode.ShouldBe(0);
        run.Stdout.ShouldStartWith($"gpsd-exporter {BuildInfo.Version} (revision ");
        run.Stdout.ShouldContain(BuildInfo.Runtime);
    }

    [Fact]
    public async Task Help_prints_usage_and_exits_0()
    {
        var run = await RunToExitAsync(["--help"]);

        run.ExitCode.ShouldBe(0);
        run.Stdout.ShouldContain("Usage: gpsd-exporter");
    }

    [Theory]
    [InlineData(new[] { "-d", "[::1" }, "Gpsd:Address '[::1' is not host:port")]
    [InlineData(new[] { "--gpsd=[::1" }, "Gpsd:Address '[::1' is not host:port")]
    [InlineData(new[] { "-l", "example.com:9978" }, "Http:Listen 'example.com:9978' is not [addr]:port")]
    [InlineData(new[] { "--device=" }, "Gpsd:Device is empty")]
    [InlineData(new[] { "--pps=maybe" }, "'maybe' at 'Gpsd:Pps'")]
    [InlineData(new[] { "--log-format", "xml" }, "Logging:Console:FormatterName (--log-format) must be text or json.")]
    [InlineData(new[] { "--Staleness:Tpv=00:00:00" }, "Staleness:Tpv must be positive.")]
    [InlineData(new[] { "--Gpsd:ReconnectMaxDelay=00:00:00.5" }, "Gpsd:ReconnectMaxDelay must not be less than Gpsd:ReconnectInitialDelay.")]
    [InlineData(new[] { "--Http:MetricsPath=metrics" }, "Http:MetricsPath 'metrics' must be a path starting with '/'.")]
    [InlineData(new[] { "--Gpsd:ConnectTimeout=soon" }, "'soon' at 'Gpsd:ConnectTimeout'")]
    [InlineData(new[] { "--Gpsd:Adress=gpsd:2947" }, "'Adress'")]
    [InlineData(new[] { "--Staleness:Gsv=00:00:10" }, "'Gsv'")]
    [InlineData(new[] { "-x=1" }, "'-x=1'")]
    public async Task Invalid_command_line_or_configuration_exits_2_with_a_clear_message(string[] args, string message)
    {
        var run = await RunToExitAsync(["-l", "127.0.0.1:0", .. args]);

        run.ExitCode.ShouldBe(2);
        run.Stderr.ShouldStartWith("gpsd-exporter: ");
        run.Stderr.ShouldContain(message);
    }

    [Fact]
    public async Task Every_configuration_problem_is_reported()
    {
        var run = await RunToExitAsync(["-d", "[::1", "-l", "example.com:1", "--Staleness:Sky=00:00:00"]);

        run.ExitCode.ShouldBe(2);
        run.Stderr.ShouldContain("Gpsd:Address");
        run.Stderr.ShouldContain("Http:Listen");
        run.Stderr.ShouldContain("Staleness:Sky");
    }

    [Fact]
    public async Task An_invalid_environment_variable_exits_2()
    {
        var run = await RunToExitAsync(["-l", "127.0.0.1:0"], new() { ["GPSD_EXPORTER_GPSD__ADDRESS"] = "[nope" });

        run.ExitCode.ShouldBe(2);
        run.Stderr.ShouldContain("Gpsd:Address '[nope' is not host:port");
    }

    [Fact]
    public async Task The_command_line_overrides_the_environment()
    {
        var run = await RunToExitAsync(["-l", "127.0.0.1:0", "-d", "[flag"], new() { ["GPSD_EXPORTER_GPSD__ADDRESS"] = "[environment" });

        run.ExitCode.ShouldBe(2);
        run.Stderr.ShouldContain("'[flag'");
        run.Stderr.ShouldNotContain("[environment");
    }

    [Fact]
    public async Task A_port_that_cannot_be_bound_exits_1()
    {
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        var port = ((IPEndPoint)taken.LocalEndpoint).Port;

        var run = await RunToExitAsync(["-l", $"127.0.0.1:{port}", "-d", "127.0.0.1:1"]);

        run.ExitCode.ShouldBe(1);
        run.Stderr.ShouldStartWith("gpsd-exporter: ");
        run.Stderr.ShouldContain(port.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Listen_flag_sets_the_listen_address()
    {
        var output = await RunUntilListeningAsync(["-l", "127.0.0.1:0"]);

        output.ShouldMatch(@"Now listening on: http://127\.0\.0\.1:\d+");
    }

    [Fact]
    public async Task Without_a_listen_address_ASPNETCORE_URLS_is_used()
    {
        var output = await RunUntilListeningAsync([], new() { ["ASPNETCORE_URLS"] = "http://127.0.0.1:0" });

        output.ShouldMatch(@"Now listening on: http://127\.0\.0\.1:\d+");
    }

    [Fact]
    public async Task An_explicit_listen_address_wins_over_ASPNETCORE_URLS()
    {
        var output = await RunUntilListeningAsync(["-l", "127.0.0.1:0"], new() { ["ASPNETCORE_URLS"] = "http://localhost:1" });

        output.ShouldMatch(@"Now listening on: http://127\.0\.0\.1:\d+");
        output.ShouldNotContain("Now listening on: http://localhost");
    }

    [Fact]
    public async Task Go_style_flags_set_verbosity_and_warn_about_p()
    {
        var output = await RunUntilListeningAsync(["-d", "127.0.0.1:1", "-l", "127.0.0.1:0", "-v", "-p", "10s"]);

        output.ShouldContain("-p is ignored");
        output.ShouldContain("dbug:");
    }

    [Fact]
    public async Task Log_format_json_writes_json()
    {
        var output = await RunUntilListeningAsync(["-l", "127.0.0.1:0", "--log-format", "json"]);

        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldAllBe(line => line.StartsWith('{'));
    }

    [Fact]
    public async Task Go_flags_against_FakeGpsd_connect_and_serve_metrics()
    {
        await using var gpsd = FakeGpsd.CreateStarted();
        using var process = Start(["-d", gpsd.Address, "-l", "127.0.0.1:0", "-v", "-p", "10s"], null);
        var output = new OutputCollector(process);
        try
        {
            var connection = await gpsd.AcceptAsync(Ct);
            await connection.WaitForLineAsync("""?WATCH={"enable":true,"json":true,"pps":true};""", Ct);
            using var http = new HttpClient { BaseAddress = await output.ListeningUrlAsync() };

            var deadline = DateTime.UtcNow + Limit;
            PrometheusText scrape;
            do
            {
                scrape = PrometheusText.Parse(await http.GetStringAsync(new Uri("/metrics", UriKind.Relative), Ct));
            }
            while (scrape.Value("gpsd_up") != 1 && DateTime.UtcNow < deadline && await Delay());

            scrape.Value("gpsd_up").ShouldBe(1);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_client_sending_headers_slowly_is_closed_after_the_header_timeout()
    {
        using var process = Start(["-l", "127.0.0.1:0", "-d", "127.0.0.1:1"], null);
        var output = new OutputCollector(process);
        try
        {
            var url = await output.ListeningUrlAsync();
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

    private static Process Start(string[] args, Dictionary<string, string>? environment)
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
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("GPSD_EXPORTER_", StringComparison.Ordinal) || k.StartsWith("ASPNETCORE_", StringComparison.Ordinal) || k.StartsWith("DOTNET_", StringComparison.Ordinal) || k.StartsWith("OTEL_", StringComparison.Ordinal)).ToList())
        {
            info.Environment.Remove(key);
        }

        foreach (var (key, value) in environment ?? [])
        {
            info.Environment[key] = value;
        }

        return Process.Start(info) ?? throw new InvalidOperationException("The exporter did not start.");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunToExitAsync(string[] args, Dictionary<string, string>? environment = null)
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

    /// <summary>Runs the exporter until it logs where it listens, then stops it; returns its output so far.</summary>
    private static async Task<string> RunUntilListeningAsync(string[] args, Dictionary<string, string>? environment = null)
    {
        using var process = Start(args, environment);
        var output = new StringBuilder();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(line);
            }

            if (ListeningOn().IsMatch(line))
            {
                listening.TrySetResult();
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await listening.Task.WaitAsync(Limit, Ct);
        }
        catch (TimeoutException)
        {
            lock (output)
            {
                throw new TimeoutException($"The exporter did not log where it listens. Output:\n{output}");
            }
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        lock (output)
        {
            return output.ToString();
        }
    }

    [GeneratedRegex("Now listening on: ")]
    private static partial Regex ListeningOn();

    [GeneratedRegex(@"Now listening on: (?<url>http://[^\s]+)")]
    private static partial Regex ListeningUrl();

    /// <summary>Collects a running exporter's stdout and stderr as they arrive.</summary>
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

        private string Text
        {
            get
            {
                lock (_lock)
                {
                    return _text.ToString();
                }
            }
        }

        /// <summary>Waits until the exporter logs where it listens, and returns that address.</summary>
        public async Task<Uri> ListeningUrlAsync()
        {
            var deadline = DateTime.UtcNow + Limit;
            while (DateTime.UtcNow < deadline)
            {
                if (ListeningUrl().Match(Text) is { Success: true } match)
                {
                    return new Uri(match.Groups["url"].Value);
                }

                await Task.Delay(20, Ct);
            }

            throw new TimeoutException($"The exporter did not log where it listens. Output:\n{Text}");
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
