namespace GpsdExporter.Configuration;

/// <summary>
/// The <c>Otlp</c> configuration section. When OTLP is on, the exporter's own metrics, .NET
/// runtime and process metrics and logs are exported over OTLP; gpsd metrics never are (spec,
/// open questions 7 and 8).
/// </summary>
internal sealed record OtlpOptions
{
    public const string Section = "Otlp";

    /// <summary>The prefix of the standard OpenTelemetry variables.</summary>
    public const string StandardPrefix = "OTEL_";

    /// <summary>The standard variables that also turn OTLP on, as the OpenTelemetry SDK reads them.</summary>
    public static readonly IReadOnlyList<string> StandardEndpointKeys =
    [
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
    ];

    /// <summary>
    /// <c>Otlp:Endpoint</c>: the collector's base URL, e.g. <c>http://collector:4317</c>. Other
    /// settings (protocol, headers, resource attributes) come from the standard
    /// <c>OTEL_*</c> variables.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// The standard <c>OTEL_*</c> variables that are set, by name. Not bound from the section;
    /// used to say at startup where OTLP goes, or why it is off.
    /// </summary>
    internal IReadOnlyDictionary<string, string> Standard { get; init; } = new Dictionary<string, string>();

    /// <summary>Whether one of <see cref="StandardEndpointKeys"/> is set.</summary>
    internal bool StandardEndpoint => StandardEndpointKeys.Any(Standard.ContainsKey);

    public bool Enabled => !string.IsNullOrEmpty(Endpoint) || StandardEndpoint;

    /// <summary>The standard <c>OTEL_*</c> variables in <paramref name="configuration"/> that have a value.</summary>
    public static IReadOnlyDictionary<string, string> ReadStandard(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var standard = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in configuration.GetChildren())
        {
            if (child.Key.StartsWith(StandardPrefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(child.Value))
            {
                standard[child.Key.ToUpperInvariant()] = child.Value;
            }
        }

        return standard;
    }

    public IReadOnlyList<string> Validate() =>
        string.IsNullOrEmpty(Endpoint) || Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? []
            : [$"Otlp:Endpoint '{Endpoint}' must be an absolute http or https URL."];

    public bool Equals(OtlpOptions? other) =>
        other is not null
        && string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal)
        && Standard.Count == other.Standard.Count
        && Standard.All(kv => other.Standard.TryGetValue(kv.Key, out var value) && string.Equals(kv.Value, value, StringComparison.Ordinal));

    public override int GetHashCode() => HashCode.Combine(Endpoint, Standard.Count);
}
