using System.Net;
using Microsoft.Extensions.Logging.Console;

namespace GpsdExporter.Configuration;

internal static class HostConfiguration
{
    /// <summary>
    /// Adds the exporter's configuration sources above the defaults (<c>appsettings.json</c>
    /// and unprefixed environment variables): <c>GPSD_EXPORTER_</c> environment variables,
    /// then the command line, which wins.
    /// </summary>
    public static IConfigurationBuilder AddExporterSources(this IConfigurationBuilder configuration, IReadOnlyList<string> commandLineArgs)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commandLineArgs);
        return configuration
            .AddEnvironmentVariables(ExporterSettings.EnvironmentPrefix)
            .AddCommandLine([.. commandLineArgs]);
    }

    /// <summary>Applies the listen address and the console log format.</summary>
    public static WebApplicationBuilder ConfigureExporterHost(this WebApplicationBuilder builder, ExporterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);

        if (!HttpOptions.TryParseListen(settings.Http.Listen, out var listen))
        {
            throw new ArgumentException($"Http:Listen '{settings.Http.Listen}' was not validated.", nameof(settings));
        }

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            switch (listen)
            {
                case { Localhost: true }:
                    kestrel.ListenLocalhost(listen.Port);
                    break;
                case { Address: { } address }:
                    kestrel.Listen(address, listen.Port);
                    break;
                default:
                    kestrel.ListenAnyIP(listen!.Port);
                    break;
            }
        });

        builder.Logging.ClearProviders();
        if (settings.LogFormat == LogFormat.Json)
        {
            builder.Logging.AddJsonConsole(json =>
            {
                json.UseUtcTimestamp = true;
                json.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            });
        }
        else
        {
            builder.Logging.AddSimpleConsole(text =>
            {
                text.SingleLine = true;
                text.UseUtcTimestamp = true;
                text.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
                text.ColorBehavior = LoggerColorBehavior.Disabled;
            });
        }

        return builder;
    }
}

internal static partial class StartupLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "-p is ignored: the exporter streams from gpsd with ?WATCH and does not poll.")]
    public static partial void PollIgnored(ILogger logger);
}
