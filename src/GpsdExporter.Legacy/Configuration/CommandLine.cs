namespace GpsdExporter.Configuration;

/// <summary>The command line, translated into configuration (AC11.2).</summary>
/// <param name="ConfigArgs">
/// <c>--Section:Key=value</c> arguments for the highest-precedence configuration source.
/// </param>
/// <param name="HostArgs">Host settings (<c>--environment</c>, <c>--contentRoot</c>, <c>--applicationName</c>).</param>
/// <param name="PollIgnored">Whether <c>-p</c> was given; it is accepted and ignored with a Warning.</param>
internal sealed record CommandLineResult(
    IReadOnlyList<string> ConfigArgs,
    IReadOnlyList<string> HostArgs,
    bool ShowVersion,
    bool ShowHelp,
    bool PollIgnored,
    IReadOnlyList<string> Errors);

/// <summary>
/// Translates the command line. It accepts the Go exporter's flags, so the binary is a drop-in
/// replacement (AC11.2), plus the spec's long options and any <c>--Section:Key=value</c>
/// setting. As with Go's <c>flag</c> package, a flag may start with <c>-</c> or <c>--</c> and
/// take its value as <c>-d value</c> or <c>-d=value</c>; a boolean flag takes a value only
/// after <c>=</c>.
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

    private static readonly string[] HostKeys = ["environment", "contentRoot", "applicationName"];

    public static CommandLineResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var config = new List<string>();
        var host = new List<string>();
        var errors = new List<string>();
        var showVersion = false;
        var showHelp = false;
        var pollIgnored = false;
        string? logLevel = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Length < 2 || arg[0] != '-')
            {
                errors.Add($"Unexpected argument '{arg}'.");
                continue;
            }

            var dashes = arg.StartsWith("--", StringComparison.Ordinal) ? 2 : 1;
            var body = arg[dashes..];
            var equals = body.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? body : body[..equals];
            var inline = equals < 0 ? null : body[(equals + 1)..];

            switch (name)
            {
                case "d" or "gpsd":
                    Set("Gpsd:Address");
                    break;
                case "l" or "listen":
                    Set("Http:Listen");
                    break;
                case "device":
                    Set("Gpsd:Device");
                    break;
                case "log-format":
                    Set("Logging:Format");
                    break;
                case "pps":
                    if (Flag() is { } pps)
                    {
                        config.Add($"--Gpsd:Pps={(pps ? "true" : "false")}");
                    }

                    break;
                case "v":
                    if (Flag() == true)
                    {
                        logLevel ??= "Debug";
                    }

                    break;
                case "vv":
                    if (Flag() == true)
                    {
                        logLevel = "Trace";
                    }

                    break;
                case "p":
                    // Go's poll interval. Polling no longer exists (?WATCH streams), so the
                    // value is consumed and ignored; Program logs a Warning.
                    if (Value() is not null)
                    {
                        pollIgnored = true;
                    }

                    break;
                case "version":
                    showVersion = true;
                    break;
                case "h" or "help":
                    showHelp = true;
                    break;
                default:
                    if (dashes == 2 && name.Contains(':', StringComparison.Ordinal))
                    {
                        Set(name);
                    }
                    else if (dashes == 2 && HostKeys.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        // Passed by WebApplicationFactory in tests, and harmless otherwise.
                        if (Value() is { } hostValue)
                        {
                            host.Add($"--{name}={hostValue}");
                        }
                    }
                    else
                    {
                        errors.Add($"Unknown option '{arg}'.");
                    }

                    break;
            }

            void Set(string key)
            {
                if (Value() is { } value)
                {
                    config.Add($"--{key}={value}");
                }
            }

            // The value after '=', else the next argument.
            string? Value()
            {
                if (inline is not null)
                {
                    return inline;
                }

                if (i + 1 < args.Count)
                {
                    return args[++i];
                }

                errors.Add($"Option '{arg}' needs a value.");
                return null;
            }

            // A boolean flag: true on its own, or the value after '='.
            bool? Flag()
            {
                if (inline is null)
                {
                    return true;
                }

                if (bool.TryParse(inline, out var value))
                {
                    return value;
                }

                errors.Add($"Option '{arg}' takes true or false.");
                return null;
            }
        }

        if (logLevel is not null)
        {
            config.Add($"--Logging:LogLevel:Default={logLevel}");
        }

        return new CommandLineResult(config, host, showVersion, showHelp, pollIgnored, errors);
    }
}
