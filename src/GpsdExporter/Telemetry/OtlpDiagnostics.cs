using System.Diagnostics.Tracing;
using System.Globalization;

namespace GpsdExporter.Telemetry;

/// <summary>
/// Says at startup whether the exporter's own telemetry goes over OTLP and where, and while it
/// runs, passes the OTLP exporter's warnings and errors (a collector that cannot be reached, a
/// rejected request) to the console log. The OpenTelemetry SDK reports those only on its event
/// source, so without this a wrong endpoint or protocol fails silently.
/// </summary>
internal sealed partial class OtlpDiagnostics(OtlpStatus status, ILoggerFactory loggers, TimeProvider time) : IHostedService, IDisposable
{
    /// <summary>
    /// The log category for these messages. It is kept out of OTLP log export, so a failing
    /// export does not feed itself.
    /// </summary>
    public const string Category = "GpsdExporter.Telemetry.Otlp";

    /// <summary>An event that repeats within this long of the last one logged is counted, not logged.</summary>
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger = loggers.CreateLogger(Category);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTimeOffset Logged, int Suppressed)> _recent = new(StringComparer.Ordinal);
    private ExporterEventListener? _listener;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (status.Enabled)
        {
            foreach (var signal in status.Signals)
            {
                OtlpOn(_logger, signal.Name, signal.Endpoint, signal.Protocol);
            }

            _listener = new ExporterEventListener(this);
        }
        else
        {
            OtlpOff(_logger);
        }

        foreach (var warning in status.Warnings)
        {
            ConfigurationWarning(_logger, warning);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _listener?.Dispose();

    /// <summary>
    /// Logs one exporter event, unless an event with the same <paramref name="key"/> (its id and
    /// endpoint) was logged within <see cref="RepeatInterval"/>.
    /// </summary>
    internal void Forward(string key, EventLevel level, string message)
    {
        var now = time.GetUtcNow();
        int suppressed;
        lock (_gate)
        {
            if (_recent.TryGetValue(key, out var last) && now - last.Logged < RepeatInterval)
            {
                _recent[key] = (last.Logged, last.Suppressed + 1);
                return;
            }

            suppressed = _recent.TryGetValue(key, out last) ? last.Suppressed : 0;
            _recent[key] = (now, 0);
        }

        var text = suppressed == 0 ? message : $"{message} ({suppressed} more like this since the last one logged)";
        if (level <= EventLevel.Error)
        {
            ExportError(_logger, text);
        }
        else
        {
            ExportWarning(_logger, text);
        }
    }

    /// <summary>The event's id and first payload item, which for the exporter's failures is the endpoint.</summary>
    internal static string Key(int eventId, IReadOnlyList<object?>? payload) =>
        $"{eventId}:{(payload is { Count: > 0 } ? Convert.ToString(payload[0], CultureInfo.InvariantCulture) : null)}";

    /// <summary>
    /// The event's message with its payload filled in, up to the first line break: exceptions
    /// in the payload carry their stack traces, which say nothing useful here.
    /// </summary>
    internal static string Format(int eventId, string? eventName, string? message, IReadOnlyList<object?>? payload)
    {
        string text;
        if (message is null)
        {
            text = eventName ?? $"Event {eventId}";
        }
        else
        {
            try
            {
                text = payload is { Count: > 0 }
                    ? string.Format(CultureInfo.InvariantCulture, message, [.. payload])
                    : message;
            }
            catch (FormatException)
            {
                text = message;
            }
        }

        var lineBreak = text.IndexOfAny(['\r', '\n']);
        return (lineBreak < 0 ? text : text[..lineBreak]).TrimEnd();
    }

    [LoggerMessage(1, LogLevel.Information, "OTLP export on: {Signal} to {Endpoint} over {Protocol}")]
    private static partial void OtlpOn(ILogger logger, string signal, Uri endpoint, string protocol);

    [LoggerMessage(2, LogLevel.Debug, "OTLP export off: no OTLP endpoint is set")]
    private static partial void OtlpOff(ILogger logger);

    [LoggerMessage(3, LogLevel.Warning, "{Warning}")]
    private static partial void ConfigurationWarning(ILogger logger, string warning);

    [LoggerMessage(4, LogLevel.Warning, "OTLP export: {Message}")]
    private static partial void ExportWarning(ILogger logger, string message);

    [LoggerMessage(5, LogLevel.Error, "OTLP export failed: {Message}")]
    private static partial void ExportError(ILogger logger, string message);

    /// <summary>Listens to the OTLP exporter's event source at warning level and above.</summary>
    private sealed class ExporterEventListener : EventListener
    {
        public const string SourceName = "OpenTelemetry-Exporter-OpenTelemetryProtocol";

        private readonly OtlpDiagnostics _owner;

        public ExporterEventListener(OtlpDiagnostics owner)
        {
            _owner = owner;

            // OnEventSourceCreated runs from the base constructor, before _owner is set, for
            // sources that already exist; enable those here.
            foreach (var source in EventSource.GetSources())
            {
                OnEventSourceCreated(source);
            }
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (_owner is not null && eventSource.Name == SourceName)
            {
                EnableEvents(eventSource, EventLevel.Warning);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (_owner is not null && eventData.EventSource.Name == SourceName && eventData.Level is > 0 and <= EventLevel.Warning)
            {
                _owner.Forward(
                    Key(eventData.EventId, eventData.Payload),
                    eventData.Level,
                    Format(eventData.EventId, eventData.EventName, eventData.Message, eventData.Payload));
            }
        }
    }
}
