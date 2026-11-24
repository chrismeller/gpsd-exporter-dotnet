using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace GpsdExporter.Configuration;

internal static class HostConfiguration
{
    /// <summary>The prefix of the exporter's environment variables, e.g. <c>GPSD_EXPORTER_GPSD__ADDRESS</c>.</summary>
    public const string EnvironmentPrefix = "GPSD_EXPORTER_";

    private const string UtcTimestamp = "yyyy-MM-ddTHH:mm:ss.fffZ";

    private const string ListenKey = $"{HttpOptions.Section}:{nameof(HttpOptions.Listen)}";

    /// <summary>
    /// Adds the exporter's configuration sources above the defaults (<c>appsettings.json</c>
    /// and unprefixed environment variables): <c>GPSD_EXPORTER_</c> environment variables,
    /// then the command line, which wins.
    /// </summary>
    /// <exception cref="FormatException">The command line has a single-dash flag that is not one of the exporter's.</exception>
    public static IConfigurationBuilder AddExporterSources(this IConfigurationBuilder configuration, IReadOnlyList<string> commandLineArgs) =>
        configuration
            .AddEnvironmentVariables(EnvironmentPrefix)
            .AddExporterCommandLine(commandLineArgs);

    /// <summary>The listen address and the console log format.</summary>
    public static WebApplicationBuilder ConfigureExporterHost(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<KestrelServerOptions>()
            .Configure<IConfiguration>(static (kestrel, configuration) => Listen(kestrel, configuration));

        // The console formatter comes from Logging:Console:FormatterName (--log-format): text
        // (the console logger's simple formatter, also its fallback) or json. The slim builder
        // fixes the formatter to simple, so the section is bound again after it.
        builder.Logging.AddConsole();
        builder.Services.Configure<ConsoleLoggerOptions>(builder.Configuration.GetSection(LogFormatOptions.Section));
        builder.Services.AddOptions<LogFormatOptions>()
            .BindConfiguration(LogFormatOptions.Section)
            .Validate(static format => format.IsValid, $"{CommandLine.LogFormatKey} (--log-format) must be text or json.")
            .ValidateOnStart();
        builder.Services.Configure<SimpleConsoleFormatterOptions>(static text =>
        {
            text.SingleLine = true;
            text.UseUtcTimestamp = true;
            text.TimestampFormat = UtcTimestamp + " ";
            text.ColorBehavior = LoggerColorBehavior.Disabled;
        });
        builder.Services.Configure<JsonConsoleFormatterOptions>(static json =>
        {
            json.UseUtcTimestamp = true;
            json.TimestampFormat = UtcTimestamp;
        });

        return builder;
    }

    /// <summary>
    /// An explicit <c>Http:Listen</c> wins. Without one, <c>ASPNETCORE_URLS</c> (as Aspire sets
    /// it) is left to Kestrel; without either, the exporter listens on <c>:9978</c>.
    /// </summary>
    /// <remarks>
    /// This reads the key rather than <see cref="IOptions{HttpOptions}"/>: Kestrel's options are
    /// built with the application, before the options are validated, and an invalid value is
    /// reported by that validation instead of here.
    /// </remarks>
    private static void Listen(KestrelServerOptions kestrel, IConfiguration configuration)
    {
        var listen = configuration[ListenKey];
        if (listen is null && !string.IsNullOrEmpty(configuration[WebHostDefaults.ServerUrlsKey]))
        {
            return;
        }

        if (!HttpOptions.TryParseListen(listen ?? HttpOptions.DefaultListen, out var address))
        {
            return;
        }

        switch (address)
        {
            case { Localhost: true }:
                kestrel.ListenLocalhost(address.Port);
                break;
            case { Address: { } ip }:
                kestrel.Listen(ip, address.Port);
                break;
            default:
                kestrel.ListenAnyIP(address!.Port);
                break;
        }
    }
}
