namespace GpsdExporter.State;

/// <summary>A value with the time the exporter received it (exporter clock), for staleness.</summary>
public readonly record struct Timed<T>(T Value, DateTimeOffset ReceivedAt);
