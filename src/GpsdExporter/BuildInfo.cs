using System.Reflection;
using System.Runtime.InteropServices;

namespace GpsdExporter;

/// <summary>
/// The exporter's version, source revision and runtime, for <c>gpsd_exporter_build_info</c>.
/// The SDK writes the version and the git commit into the informational version as
/// <c>version+commit</c>.
/// </summary>
internal static class BuildInfo
{
    public const string UnknownRevision = "unknown";

    static BuildInfo()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        (Version, Revision) = Split(informational);
    }

    /// <summary>The version, without build metadata (from the release tag in CI).</summary>
    public static string Version { get; }

    /// <summary>The git commit the binary was built from, or <c>unknown</c>.</summary>
    public static string Revision { get; }

    /// <summary>The .NET runtime, e.g. <c>.NET 10.0.0</c>.</summary>
    public static string Runtime { get; } = RuntimeInformation.FrameworkDescription;

    /// <summary>Splits <c>version+metadata</c>; the metadata, if any, is the revision.</summary>
    internal static (string Version, string Revision) Split(string informationalVersion)
    {
        ArgumentNullException.ThrowIfNull(informationalVersion);
        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return plus < 0
            ? (informationalVersion, UnknownRevision)
            : (informationalVersion[..plus], plus + 1 < informationalVersion.Length ? informationalVersion[(plus + 1)..] : UnknownRevision);
    }
}
