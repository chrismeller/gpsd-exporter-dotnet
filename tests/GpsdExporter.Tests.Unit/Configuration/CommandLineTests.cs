using GpsdExporter.Configuration;

namespace GpsdExporter.Tests.Unit.Configuration;

/// <summary>AC11.2: the Go exporter's flags, plus the spec's long options.</summary>
public sealed class CommandLineTests
{
    [Fact]
    public void No_arguments_means_no_overrides()
    {
        var result = CommandLine.Parse([]);

        result.ConfigArgs.ShouldBeEmpty();
        result.HostArgs.ShouldBeEmpty();
        result.Errors.ShouldBeEmpty();
        result.ShowVersion.ShouldBeFalse();
        result.ShowHelp.ShouldBeFalse();
        result.PollIgnored.ShouldBeFalse();
    }

    [Theory]
    [InlineData("-d", "gpsd.lan:2948")]
    [InlineData("--d", "gpsd.lan:2948")]
    [InlineData("--gpsd", "gpsd.lan:2948")]
    [InlineData("-d", "[::1]:2947")]
    public void Gpsd_address_flag_takes_the_next_argument(string flag, string value) =>
        Config(flag, value).ShouldBe([$"--Gpsd:Address={value}"]);

    [Theory]
    [InlineData("-d=gpsd.lan:2948")]
    [InlineData("--gpsd=gpsd.lan:2948")]
    public void Gpsd_address_flag_takes_a_value_after_equals(string arg) =>
        Config(arg).ShouldBe(["--Gpsd:Address=gpsd.lan:2948"]);

    [Theory]
    [InlineData("-l", ":9978", "--Http:Listen=:9978")]
    [InlineData("-l", ":0", "--Http:Listen=:0")]
    [InlineData("--listen", "127.0.0.1:9100", "--Http:Listen=127.0.0.1:9100")]
    [InlineData("--device", "/dev/ttyACM0", "--Gpsd:Device=/dev/ttyACM0")]
    [InlineData("--log-format", "json", "--Logging:Format=json")]
    [InlineData("--Staleness:Tpv", "00:00:15", "--Staleness:Tpv=00:00:15")]
    public void Value_options_map_to_settings(string flag, string value, string expected) =>
        Config(flag, value).ShouldBe([expected]);

    [Fact]
    public void Any_section_key_setting_passes_through() =>
        Config("--Metrics:Satellites=false", "--Gpsd:KeepaliveIdle=00:00:20")
            .ShouldBe(["--Metrics:Satellites=false", "--Gpsd:KeepaliveIdle=00:00:20"]);

    [Theory]
    [InlineData(new[] { "-v" }, "Debug")]
    [InlineData(new[] { "-vv" }, "Trace")]
    [InlineData(new[] { "-v", "-vv" }, "Trace")]
    [InlineData(new[] { "-vv", "-v" }, "Trace")]
    [InlineData(new[] { "--v" }, "Debug")]
    [InlineData(new[] { "-v=true" }, "Debug")]
    public void Verbosity_sets_the_default_log_level(string[] args, string level) =>
        Config(args).ShouldBe([$"--Logging:LogLevel:Default={level}"]);

    [Fact]
    public void Verbosity_false_changes_nothing() => Config("-v=false").ShouldBeEmpty();

    [Theory]
    [InlineData(new[] { "--pps" }, "true")]
    [InlineData(new[] { "--pps=true" }, "true")]
    [InlineData(new[] { "--pps=false" }, "false")]
    [InlineData(new[] { "-pps=False" }, "false")]
    public void Pps_is_a_boolean_flag(string[] args, string value) =>
        Config(args).ShouldBe([$"--Gpsd:Pps={value}"]);

    [Theory]
    [InlineData("-p", "10s")]
    [InlineData("-p", "1m")]
    public void Poll_interval_is_accepted_and_ignored(string flag, string value)
    {
        var result = CommandLine.Parse([flag, value, "-d", "localhost:2947"]);

        result.Errors.ShouldBeEmpty();
        result.PollIgnored.ShouldBeTrue();
        result.ConfigArgs.ShouldBe(["--Gpsd:Address=localhost:2947"]);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-version")]
    public void Version_flag(string arg) => CommandLine.Parse([arg]).ShowVersion.ShouldBeTrue();

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("-help")]
    public void Help_flag(string arg) => CommandLine.Parse([arg]).ShowHelp.ShouldBeTrue();

    [Fact]
    public void Host_settings_from_the_test_host_are_passed_to_the_host()
    {
        var result = CommandLine.Parse(["--applicationName=GpsdExporter", "--environment", "Development"]);

        result.Errors.ShouldBeEmpty();
        result.ConfigArgs.ShouldBeEmpty();
        result.HostArgs.ShouldBe(["--applicationName=GpsdExporter", "--environment=Development"]);
    }

    [Theory]
    [InlineData(new[] { "-x" }, "Unknown option '-x'.")]
    [InlineData(new[] { "--frobnicate=1" }, "Unknown option '--frobnicate=1'.")]
    [InlineData(new[] { "positional" }, "Unexpected argument 'positional'.")]
    [InlineData(new[] { "-" }, "Unexpected argument '-'.")]
    [InlineData(new[] { "-d" }, "Option '-d' needs a value.")]
    [InlineData(new[] { "-p" }, "Option '-p' needs a value.")]
    [InlineData(new[] { "--pps=maybe" }, "Option '--pps=maybe' takes true or false.")]
    [InlineData(new[] { "-v=loud" }, "Option '-v=loud' takes true or false.")]
    public void Bad_arguments_are_errors(string[] args, string error) =>
        CommandLine.Parse(args).Errors.ShouldBe([error]);

    [Fact]
    public void Go_style_command_line_parses()
    {
        var result = CommandLine.Parse(["-d", "127.0.0.1:2947", "-l", ":9978", "-v", "-p", "10s"]);

        result.Errors.ShouldBeEmpty();
        result.PollIgnored.ShouldBeTrue();
        result.ConfigArgs.ShouldBe(["--Gpsd:Address=127.0.0.1:2947", "--Http:Listen=:9978", "--Logging:LogLevel:Default=Debug"]);
    }

    private static IReadOnlyList<string> Config(params string[] args)
    {
        var result = CommandLine.Parse(args);
        result.Errors.ShouldBeEmpty();
        return result.ConfigArgs;
    }
}
