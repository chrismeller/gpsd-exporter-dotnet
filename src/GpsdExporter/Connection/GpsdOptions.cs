using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GpsdExporter.Connection;

/// <summary>
/// How to reach gpsd and keep the connection healthy (spec R1, the <c>Gpsd</c> configuration
/// section). The defaults are the spec's.
/// </summary>
internal sealed record GpsdOptions
{
    public const string Section = "Gpsd";

    public const int DefaultPort = 2947;

    /// <summary><c>gpsd_json(5)</c>: a command line must be 80 characters or fewer (AC1.3).</summary>
    public const int MaxCommandLength = 80;

    /// <summary><c>host:port</c>; an IPv6 address goes in brackets. The port defaults to 2947.</summary>
    public string Address { get; set; } = "localhost:2947";

    /// <summary>Watch only this device. Null watches every device.</summary>
    public string? Device { get; set; }

    /// <summary>Ask gpsd for PPS and TOFF objects.</summary>
    public bool Pps { get; set; } = true;

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan ReconnectInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Silence before <c>?VERSION;</c> is sent (AC1.4).</summary>
    public TimeSpan KeepaliveIdle { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait for any bytes after the keepalive probe (AC1.4).</summary>
    public TimeSpan KeepaliveTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The one command sent after connecting (AC1.3), without its line terminator:
    /// <c>?WATCH={"enable":true,"json":true,"pps":true};</c>, plus <c>"device"</c> when set.
    /// </summary>
    public string WatchCommand
    {
        get
        {
            var pps = Pps ? ",\"pps\":true" : string.Empty;
            var device = Device is null ? string.Empty : $",\"device\":{JsonString(Device)}";
            return $"?WATCH={{\"enable\":true,\"json\":true{pps}{device}}};";
        }
    }

    /// <summary>Sent on shutdown while connected (AC1.8).</summary>
    public const string UnwatchCommand = "?WATCH={\"enable\":false};";

    /// <summary>The keepalive probe (AC1.4).</summary>
    public const string VersionCommand = "?VERSION;";

    /// <summary>Every problem with these options; empty when they are valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!TryParseAddress(Address, out _, out _))
        {
            errors.Add($"Gpsd:Address '{Address}' is not host:port (IPv6 addresses go in brackets).");
        }

        if (Device is { Length: 0 })
        {
            errors.Add("Gpsd:Device is empty; leave it unset to watch every device.");
        }

        if (WatchCommand.Length > MaxCommandLength)
        {
            errors.Add(
                $"Gpsd:Device is too long: the WATCH command would be {WatchCommand.Length} characters, " +
                $"and gpsd accepts at most {MaxCommandLength}.");
        }

        Positive(ConnectTimeout, nameof(ConnectTimeout));
        Positive(ReconnectInitialDelay, nameof(ReconnectInitialDelay));
        Positive(ReconnectMaxDelay, nameof(ReconnectMaxDelay));
        Positive(KeepaliveIdle, nameof(KeepaliveIdle));
        Positive(KeepaliveTimeout, nameof(KeepaliveTimeout));
        if (ReconnectMaxDelay < ReconnectInitialDelay)
        {
            errors.Add("Gpsd:ReconnectMaxDelay must not be less than Gpsd:ReconnectInitialDelay.");
        }

        return errors;

        void Positive(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero)
            {
                errors.Add($"Gpsd:{name} must be positive.");
            }
        }
    }

    /// <summary>Splits <c>host:port</c>, <c>[v6]:port</c>, <c>host</c> or <c>[v6]</c>.</summary>
    public static bool TryParseAddress(string? address, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        string? portText;
        if (address.StartsWith('['))
        {
            var close = address.IndexOf(']', StringComparison.Ordinal);
            if (close < 2)
            {
                return false;
            }

            host = address[1..close];
            var rest = address[(close + 1)..];
            if (rest.Length == 0)
            {
                return true;
            }

            if (rest[0] != ':')
            {
                return false;
            }

            portText = rest[1..];
        }
        else
        {
            var colon = address.LastIndexOf(':');
            if (colon < 0)
            {
                host = address;
                return true;
            }

            if (address.IndexOf(':', StringComparison.Ordinal) != colon)
            {
                // A bare IPv6 address: the port would be ambiguous.
                return false;
            }

            host = address[..colon];
            portText = address[(colon + 1)..];
        }

        return host.Length > 0
            && int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is > 0 and <= 65535;
    }

    private static string JsonString(string value)
    {
        using var buffer = new MemoryStream();
        // Minimal escaping (\" and \\ rather than \u0022): gpsd's JSON parser is small.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStringValue(value);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
