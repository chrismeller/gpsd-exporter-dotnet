namespace GpsdExporter.Configuration;

/// <summary>
/// The <c>Otlp</c> configuration section. When OTLP is on, the exporter's own metrics, .NET
/// runtime and process metrics and logs are exported over OTLP; gpsd metrics never are (spec,
/// open questions 7 and 8).
/// </summary>
internal sealed record OtlpOptions
{
    public const string Section = "Otlp";

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

    /// <summary>Whether one of <see cref="StandardEndpointKeys"/> is set. Not bound from the section.</summary>
    internal bool StandardEndpoint { get; init; }

    public bool Enabled => !string.IsNullOrEmpty(Endpoint) || StandardEndpoint;

    public IReadOnlyList<string> Validate() =>
        string.IsNullOrEmpty(Endpoint) || Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? []
            : [$"Otlp:Endpoint '{Endpoint}' must be an absolute http or https URL."];
}
