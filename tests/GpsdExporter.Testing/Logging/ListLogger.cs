using Microsoft.Extensions.Logging;

namespace GpsdExporter.Testing.Logging;

/// <summary>Records log entries so tests can assert on them.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    private readonly Lock _gate = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_gate)
        {
            _entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }
}

/// <summary>One recorded log entry.</summary>
public sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
