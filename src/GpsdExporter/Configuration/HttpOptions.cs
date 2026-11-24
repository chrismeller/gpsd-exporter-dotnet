using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace GpsdExporter.Configuration;

/// <summary>Where the HTTP server listens: an IP address (null for every address) or <c>localhost</c>, and a port.</summary>
internal sealed record ListenAddress(IPAddress? Address, bool Localhost, int Port);

/// <summary>The <c>Http</c> configuration section.</summary>
internal sealed class HttpOptions : IValidatableObject
{
    public const string Section = "Http";

    /// <summary>The listen address when neither <see cref="Listen"/> nor <c>ASPNETCORE_URLS</c> is set.</summary>
    public const string DefaultListen = ":9978";

    /// <summary>
    /// <c>[addr]:port</c>, as the Go exporter's <c>-l</c>: <c>:9978</c> listens on every address.
    /// Unset, the exporter listens on <c>ASPNETCORE_URLS</c> when that is set (as under Aspire),
    /// otherwise on <see cref="DefaultListen"/>.
    /// </summary>
    public string? Listen { get; set; }

    public string MetricsPath { get; set; } = "/metrics";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Listen is not null && !TryParseListen(Listen, out _))
        {
            yield return new ValidationResult(
                $"Http:Listen '{Listen}' is not [addr]:port (an IP address or localhost; IPv6 in brackets; port 0 to 65535).");
        }

        if (string.IsNullOrEmpty(MetricsPath) || MetricsPath[0] != '/' || MetricsPath.Contains('?', StringComparison.Ordinal))
        {
            yield return new ValidationResult($"Http:MetricsPath '{MetricsPath}' must be a path starting with '/'.");
        }
    }

    /// <summary>
    /// Parses <c>:port</c>, <c>addr:port</c> or <c>[v6]:port</c>. The address is an IP literal
    /// or <c>localhost</c>; other host names are not accepted, so the exporter never listens
    /// somewhere a DNS answer chose.
    /// </summary>
    public static bool TryParseListen(string? value, out ListenAddress? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var colon = value.LastIndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        var hostText = value[..colon];
        if (!int.TryParse(value.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port > 65535)
        {
            return false;
        }

        if (hostText.Length == 0)
        {
            result = new ListenAddress(null, false, port);
            return true;
        }

        if (hostText.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            result = new ListenAddress(null, true, port);
            return true;
        }

        var bracketed = hostText.StartsWith('[') && hostText.EndsWith(']');
        var literal = bracketed ? hostText[1..^1] : hostText;
        if (!IPAddress.TryParse(literal, out var address)
            || (address.AddressFamily == AddressFamily.InterNetworkV6) != bracketed)
        {
            return false;
        }

        result = new ListenAddress(address, false, port);
        return true;
    }
}

/// <summary>Validates <see cref="HttpOptions"/>. Source-generated, so it is AOT-safe.</summary>
[OptionsValidator]
internal sealed partial class HttpOptionsValidator : IValidateOptions<HttpOptions>;
