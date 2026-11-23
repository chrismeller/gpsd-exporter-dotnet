using System.Runtime.CompilerServices;

namespace GpsdExporter.Tests.Metrics;

internal static class VerifyConfig
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Approved snapshots live in Snapshots/<TestClass>.<TestMethod>.verified.txt.
        Verifier.DerivePathInfo((_, projectDirectory, type, method) =>
            new PathInfo(Path.Combine(projectDirectory, "Snapshots"), type.Name, method.Name));
    }
}
