using GpsdExporter.Connection;

namespace GpsdExporter.Configuration;

/// <summary>The command line, after the flags that are not settings have been taken out.</summary>
/// <param name="ConfigArgs">The rest, for <see cref="CommandLine.AddExporterCommandLine"/>.</param>
/// <param name="PollIgnored">Whether <c>-p</c> was given; it is accepted and ignored with a Warning.</param>
internal sealed record CommandLineResult(IReadOnlyList<string> ConfigArgs, bool ShowVersion, bool ShowHelp, bool PollIgnored);

/// <summary>
/// The command line accepts the Go exporter's flags, so the binary is a drop-in replacement,
/// and any <c>--Section:Key=value</c> setting. The flags that set a value are switch mappings
/// for the standard command line configuration source; only the ones it cannot express are
/// handled here.
/// </summary>
internal static class CommandLine
{
    public const string Usage = """
        Usage: gpsd-exporter [options]

          -d, --gpsd host:port      gpsd address (default localhost:2947; IPv6 in brackets)
          -l, --listen [addr]:port  HTTP listen address (default :9978)
              --device path         watch only this device
              --pps[=true|false]    ask gpsd for PPS and TOFF (default true)
              --log-format text|json
          -v                        debug logging
          -vv                       trace logging
          -p duration               accepted for compatibility and ignored
              --Section:Key=value   any setting, e.g. --Staleness:Tpv=00:00:15
              --version             print the version and exit
          -h, --help                print this help and exit

        Settings can also come from GPSD_EXPORTER_ environment variables (GPSD_EXPORTER_GPSD__ADDRESS)
        and appsettings.json. Flags take precedence over both.
        """;

    /// <summary>The console formatter that <c>--log-format</c> sets.</summary>
    public const string LogFormatKey = "Logging:Console:FormatterName";

    /// <summary>
    /// The Go exporter's flags and the configuration keys they set. As with Go's <c>flag</c>
    /// package, each may start with <c>-</c> or <c>--</c>.
    /// </summary>
    public static IDictionary<string, string> SwitchMappings { get; } = new Dictionary<string, string>
    {
        ["-d"] = $"{GpsdOptions.Section}:{nameof(GpsdOptions.Address)}",
        ["--d"] = $"{GpsdOptions.Section}:{nameof(GpsdOptions.Address)}",
        ["-gpsd"] = $"{GpsdOptions.Section}:{nameof(GpsdOptions.Address)}",
        ["--gpsd"] = $"{GpsdOptions.Section}:{nameof(GpsdOptions.Address)}",
        ["-l"] = $"{HttpOptions.Section}:{nameof(HttpOptions.Listen)}",
        ["--l"] = $"{HttpOptions.Section}:{nameof(HttpOptions.Listen)}",
        ["-listen"] = $"{HttpOptions.Section}:{nameof(HttpOptions.Listen)}",
        ["--listen"] = $"{HttpOptions.Section}:{nameof(HttpOptions.Listen)}",
        ["-device"] = $"{GpsdOptions.Section}:{nameof(GpsdOptions.Device)}",
        ["--device"] = $"{GpsdOptions.Section}:{nameof(GpsdOptions.Device)}",
        ["-pps"] = PpsKey,
        ["--pps"] = PpsKey,
        ["-log-format"] = LogFormatKey,
        ["--log-format"] = LogFormatKey,
    };

    private const string PpsKey = $"{GpsdOptions.Section}:{nameof(GpsdOptions.Pps)}";

    /// <summary>
    /// Takes out the flags that are not a key and a value: <c>-v</c> and <c>-vv</c> become the
    /// default log level, <c>-p</c> and its value are dropped, and a bare <c>--pps</c> (a
    /// boolean flag, which the configuration source would give the next argument as its value)
    /// becomes <c>--pps true</c>. Everything else is left for the configuration source.
    /// </summary>
    /// <remarks>
    /// The result also goes to the host builder, whose own command line source has no switch
    /// mappings and rejects <c>-d=value</c>, so a mapped flag's <c>=value</c> becomes a
    /// separate argument; that source ignores <c>-d value</c>.
    /// </remarks>
    public static CommandLineResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var config = new List<string>(args.Count);
        var showVersion = false;
        var showHelp = false;
        var pollIgnored = false;
        string? logLevel = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-v" or "--v":
                    logLevel ??= "Debug";
                    break;
                case "-vv" or "--vv":
                    logLevel = "Trace";
                    break;
                case "-p" or "--p":
                    // Go's poll interval. The exporter streams with ?WATCH and never polls.
                    pollIgnored = true;
                    i++;
                    break;
                case var _ when arg.StartsWith("-p=", StringComparison.Ordinal) || arg.StartsWith("--p=", StringComparison.Ordinal):
                    pollIgnored = true;
                    break;
                case "-pps" or "--pps":
                    config.AddRange([arg, "true"]);
                    break;
                case "-version" or "--version":
                    showVersion = true;
                    break;
                case "-h" or "--h" or "-help" or "--help":
                    showHelp = true;
                    break;
                case var _ when arg.IndexOf('=', StringComparison.Ordinal) is > 0 and var equals && SwitchMappings.ContainsKey(arg[..equals]):
                    config.AddRange([arg[..equals], arg[(equals + 1)..]]);
                    break;
                default:
                    config.Add(arg);
                    break;
            }
        }

        if (logLevel is not null)
        {
            config.Add($"--Logging:LogLevel:Default={logLevel}");
        }

        return new CommandLineResult(config, showVersion, showHelp, pollIgnored);
    }

    /// <summary>
    /// Adds the command line (the <see cref="CommandLineResult.ConfigArgs"/>) with the Go
    /// exporter's flags mapped to settings. Throws <see cref="FormatException"/> for a
    /// single-dash flag with a value that is not one of them.
    /// </summary>
    public static IConfigurationBuilder AddExporterCommandLine(this IConfigurationBuilder configuration, IReadOnlyList<string> args) =>
        configuration.AddCommandLine([.. args], SwitchMappings);
}
