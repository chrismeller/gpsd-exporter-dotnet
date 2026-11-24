using GpsdExporter.Configuration;

namespace GpsdExporter.Telemetry;

/// <summary>Where one signal is sent over OTLP, as the OpenTelemetry SDK will resolve it.</summary>
internal sealed record OtlpSignal(string Name, Uri Endpoint, string Protocol);

/// <summary>
/// What the exporter's own telemetry will do, worked out from <see cref="OtlpOptions"/> the way
/// the OpenTelemetry SDK resolves its settings, with warnings for settings that are valid but
/// cannot work. It is logged at startup, because the SDK itself says nothing when export fails.
/// </summary>
internal sealed record OtlpStatus(bool Enabled, IReadOnlyList<OtlpSignal> Signals, IReadOnlyList<string> Warnings)
{
    public const string Grpc = "grpc";
    public const string HttpProtobuf = "http/protobuf";

    private const int GrpcPort = 4317;
    private const int HttpPort = 4318;

    public static OtlpStatus From(OtlpOptions otlp)
    {
        ArgumentNullException.ThrowIfNull(otlp);
        var warnings = new List<string>();
        foreach (var (key, value) in otlp.Standard.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (IsQuoted(value))
            {
                warnings.Add(
                    $"{key} is wrapped in quotes, which are part of its value. "
                    + "Docker's --env-file and the list form of Compose's environment keep quotes; remove them.");
            }
        }

        if (!otlp.Enabled)
        {
            if (otlp.Standard.Count > 0)
            {
                warnings.Add(
                    $"OTLP export is off: {string.Join(", ", otlp.Standard.Keys.Order(StringComparer.Ordinal))} {(otlp.Standard.Count == 1 ? "is" : "are")} set, but no endpoint is. "
                    + "Set OTEL_EXPORTER_OTLP_ENDPOINT or Otlp:Endpoint to turn it on.");
            }

            return new OtlpStatus(false, [], warnings);
        }

        OtlpSignal[] signals = [Resolve(otlp, "metrics", warnings), Resolve(otlp, "logs", warnings)];
        foreach (var signal in signals)
        {
            var port = signal.Endpoint.Port;
            if (signal.Protocol == Grpc && port == HttpPort)
            {
                warnings.Add(
                    $"OTLP {signal.Name} go to port {HttpPort}, the usual OTLP/HTTP port, over gRPC, the .NET default. "
                    + $"If the collector expects HTTP there, set OTEL_EXPORTER_OTLP_PROTOCOL={HttpProtobuf}.");
            }
            else if (signal.Protocol == HttpProtobuf && port == GrpcPort)
            {
                warnings.Add(
                    $"OTLP {signal.Name} go to port {GrpcPort}, the usual OTLP/gRPC port, over {HttpProtobuf}. "
                    + $"If the collector expects gRPC there, set OTEL_EXPORTER_OTLP_PROTOCOL={Grpc} or use port {HttpPort}.");
            }
        }

        return new OtlpStatus(true, signals, [.. warnings.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The SDK's rules: a per-signal variable wins over the general one; <c>Otlp:Endpoint</c>,
    /// set in code, wins over both (see <see cref="OwnTelemetry.Configure"/>); the general
    /// endpoint gets the signal's path with <c>http/protobuf</c>; an unsupported protocol falls
    /// back to gRPC.
    /// </summary>
    private static OtlpSignal Resolve(OtlpOptions otlp, string signal, List<string> warnings)
    {
        var upper = signal.ToUpperInvariant();
        var protocolKey = $"OTEL_EXPORTER_OTLP_{upper}_PROTOCOL";
        if (!otlp.Standard.TryGetValue(protocolKey, out var protocolText))
        {
            protocolKey = "OTEL_EXPORTER_OTLP_PROTOCOL";
            protocolText = otlp.Standard.GetValueOrDefault(protocolKey);
        }

        var protocol = protocolText?.Trim().ToLowerInvariant() switch
        {
            null or Grpc => Grpc,
            HttpProtobuf => HttpProtobuf,
            _ => null,
        };
        if (protocol is null)
        {
            warnings.Add($"{protocolKey} '{protocolText}' is not supported, so {Grpc} is used. Use {Grpc} or {HttpProtobuf}.");
            protocol = Grpc;
        }

        var signalPath = $"v1/{signal}";
        Uri endpoint;
        if (!string.IsNullOrEmpty(otlp.Endpoint))
        {
            endpoint = OwnTelemetry.SignalEndpoint(new Uri(otlp.Endpoint, UriKind.Absolute), protocol == HttpProtobuf, signalPath);
        }
        else if (TryUri(otlp, $"OTEL_EXPORTER_OTLP_{upper}_ENDPOINT", warnings, out var signalEndpoint))
        {
            endpoint = signalEndpoint;
        }
        else if (TryUri(otlp, "OTEL_EXPORTER_OTLP_ENDPOINT", warnings, out var baseEndpoint))
        {
            endpoint = OwnTelemetry.SignalEndpoint(baseEndpoint, protocol == HttpProtobuf, signalPath);
        }
        else
        {
            endpoint = protocol == HttpProtobuf
                ? new Uri($"http://localhost:{HttpPort}/{signalPath}")
                : new Uri($"http://localhost:{GrpcPort}");
        }

        return new OtlpSignal(signal, endpoint, protocol);
    }

    private static bool IsQuoted(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && (trimmed[0] == '"' || trimmed[0] == '\'') && trimmed[^1] == trimmed[0];
    }

    private static bool TryUri(OtlpOptions otlp, string key, List<string> warnings, out Uri uri)
    {
        uri = null!;
        if (!otlp.Standard.TryGetValue(key, out var text))
        {
            return false;
        }

        if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        warnings.Add($"{key} '{text}' is not an absolute http or https URL and is ignored.");
        return false;
    }
}
