using GpsdExporter.Configuration;
using Microsoft.Extensions.Configuration;

namespace GpsdExporter.Tests.Host;

/// <summary>The Go exporter's flags set the right configuration keys.</summary>
public sealed class CommandLineTests
{
    [Theory]
    [InlineData("-d", "Gpsd:Address")]
    [InlineData("--d", "Gpsd:Address")]
    [InlineData("-gpsd", "Gpsd:Address")]
    [InlineData("--gpsd", "Gpsd:Address")]
    [InlineData("-l", "Http:Listen")]
    [InlineData("--listen", "Http:Listen")]
    [InlineData("--device", "Gpsd:Device")]
    [InlineData("--log-format", "Logging:Console:FormatterName")]
    public void Value_flags_set_their_key(string flag, string key)
    {
        Configuration(flag, "value")[key].ShouldBe("value");
        Configuration($"{flag}=value")[key].ShouldBe("value");
    }

    [Fact]
    public void Section_key_settings_pass_through() =>
        Configuration("--Staleness:Tpv=00:00:15", "--Gpsd:KeepaliveIdle", "00:00:20")
            .AsEnumerable().Where(e => e.Value is not null).ShouldBe(
                [new("Staleness:Tpv", "00:00:15"), new("Gpsd:KeepaliveIdle", "00:00:20")],
                ignoreOrder: true);

    [Theory]
    [InlineData(new[] { "--pps" }, "true")]
    [InlineData(new[] { "-pps", "-d", "gpsd:2947" }, "true")]
    [InlineData(new[] { "--pps=false" }, "false")]
    [InlineData(new[] { "-pps=False" }, "False")]
    public void Pps_is_a_boolean_flag(string[] args, string value) => Configuration(args)["Gpsd:Pps"].ShouldBe(value);

    [Theory]
    [InlineData(new[] { "-v" }, "Debug")]
    [InlineData(new[] { "-vv" }, "Trace")]
    [InlineData(new[] { "-v", "-vv" }, "Trace")]
    [InlineData(new[] { "-vv", "-v" }, "Trace")]
    public void Verbosity_sets_the_default_log_level(string[] args, string level) =>
        Configuration(args)["Logging:LogLevel:Default"].ShouldBe(level);

    [Fact]
    public void No_verbosity_leaves_the_log_level_alone() =>
        Configuration("-d", "gpsd:2947")["Logging:LogLevel:Default"].ShouldBeNull();

    [Theory]
    [InlineData("-p", "10s")]
    [InlineData("-p=10s")]
    public void Poll_interval_is_accepted_and_ignored(params string[] poll)
    {
        var commandLine = CommandLine.Parse([.. poll, "-d", "gpsd:2947"]);

        commandLine.PollIgnored.ShouldBeTrue();
        Configuration(commandLine).AsEnumerable().Where(e => e.Value is not null)
            .ShouldBe([new("Gpsd:Address", "gpsd:2947")]);
    }

    [Theory]
    [InlineData("--version", true, false)]
    [InlineData("-version", true, false)]
    [InlineData("--help", false, true)]
    [InlineData("-h", false, true)]
    [InlineData("-d", false, false)]
    public void Version_and_help(string arg, bool version, bool help)
    {
        var commandLine = CommandLine.Parse([arg]);

        commandLine.ShowVersion.ShouldBe(version);
        commandLine.ShowHelp.ShouldBe(help);
    }

    [Fact]
    public void Go_style_command_line()
    {
        var configuration = Configuration("-d", "127.0.0.1:2947", "-l", ":9978", "-v", "-p", "10s");

        configuration["Gpsd:Address"].ShouldBe("127.0.0.1:2947");
        configuration["Http:Listen"].ShouldBe(":9978");
        configuration["Logging:LogLevel:Default"].ShouldBe("Debug");
    }

    [Fact]
    public void An_unknown_single_dash_flag_with_a_value_is_an_error() =>
        Should.Throw<FormatException>(() => Configuration("-x=1"));

    private static IConfigurationRoot Configuration(params string[] args) => Configuration(CommandLine.Parse(args));

    private static IConfigurationRoot Configuration(CommandLineResult commandLine) =>
        new ConfigurationBuilder().AddExporterCommandLine(commandLine.ConfigArgs).Build();
}
