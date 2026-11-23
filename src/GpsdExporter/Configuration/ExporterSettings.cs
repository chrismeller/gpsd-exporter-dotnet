using GpsdExporter.Connection;
using GpsdExporter.Metrics;
using GpsdExporter.State;

namespace GpsdExporter.Configuration;

/// <summary>
/// Every setting in the spec's Configuration table, bound from configuration and validated
/// before the host starts. Binding uses the configuration binder source generator, so it is
/// AOT-safe. Unknown keys in the exporter's own sections are errors, so a typo is reported
/// instead of silently ignored. The option types have setters only for the binder; nothing
/// changes them after startup.
/// </summary>
internal sealed record ExporterSettings(
    GpsdOptions Gpsd,
    StalenessOptions Staleness,
    MetricsOptions Metrics,
    HttpOptions Http,
    LogFormat LogFormat)
{
    /// <summary>The prefix of the exporter's environment variables, e.g. <c>GPSD_EXPORTER_GPSD__ADDRESS</c>.</summary>
    public const string EnvironmentPrefix = "GPSD_EXPORTER_";

    public const string LogFormatKey = "Logging:Format";

    public static ExporterSettings Default { get; } = new(new GpsdOptions(), new StalenessOptions(), new MetricsOptions(), new HttpOptions(), LogFormat.Text);

    /// <summary>Binds and validates. Returns the settings, or every problem found.</summary>
    public static (ExporterSettings? Settings, IReadOnlyList<string> Errors) Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>();

        var gpsd = Bind(configuration, GpsdOptions.Section, static s => s.Get<GpsdOptions>(Strict), errors) ?? new GpsdOptions();
        var staleness = Bind(configuration, StalenessOptions.Section, static s => s.Get<StalenessOptions>(Strict), errors) ?? new StalenessOptions();
        var metrics = Bind(configuration, MetricsOptions.Section, static s => s.Get<MetricsOptions>(Strict), errors) ?? new MetricsOptions();
        var http = Bind(configuration, HttpOptions.Section, static s => s.Get<HttpOptions>(Strict), errors) ?? new HttpOptions();

        errors.AddRange(gpsd.Validate());
        errors.AddRange(staleness.Validate());
        errors.AddRange(http.Validate());

        var formatText = configuration[LogFormatKey];
        var format = formatText?.ToUpperInvariant() switch
        {
            null or "TEXT" => LogFormat.Text,
            "JSON" => LogFormat.Json,
            _ => (LogFormat?)null,
        };
        if (format is null)
        {
            errors.Add($"{LogFormatKey} '{formatText}' must be text or json.");
        }

        return errors.Count == 0
            ? (new ExporterSettings(gpsd, staleness, metrics, http, format!.Value), errors)
            : (null, errors);
    }

    private static void Strict(BinderOptions options) => options.ErrorOnUnknownConfiguration = true;

    private static T? Bind<T>(IConfiguration configuration, string section, Func<IConfigurationSection, T?> get, List<string> errors)
        where T : class
    {
        try
        {
            return get(configuration.GetSection(section));
        }
        catch (InvalidOperationException ex)
        {
            errors.Add($"Invalid {section} settings: {ex.Message}");
            return null;
        }
    }
}

/// <summary><c>Logging:Format</c>: console log format.</summary>
internal enum LogFormat
{
    Text,
    Json,
}
