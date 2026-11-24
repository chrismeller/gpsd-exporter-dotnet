namespace GpsdExporter.Configuration;

/// <summary>
/// The console log format, <c>Logging:Console:FormatterName</c> (<c>--log-format</c>). The
/// console logger itself falls back to text for a name it does not know; binding the name here
/// as well lets a typo stop start-up with the other configuration errors.
/// </summary>
internal sealed class LogFormatOptions
{
    public const string Section = "Logging:Console";

    /// <summary><c>text</c> is the exporter's name for the console logger's <c>simple</c> formatter.</summary>
    private static readonly string[] Known = ["text", "simple", "json", "systemd"];

    public string? FormatterName { get; set; }

    public bool IsValid => FormatterName is null || Known.Contains(FormatterName, StringComparer.OrdinalIgnoreCase);
}
