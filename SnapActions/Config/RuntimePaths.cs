using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SnapActions.Config;

/// <summary>A separate data directory also isolates every instance identifier used by test builds.</summary>
internal static class RuntimePaths
{
    private static readonly string DefaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapActions");
    internal static string DataDirectory { get; } = ResolveDataDirectory();
    internal static bool IsIsolated { get; } = !DataDirectory.Equals(DefaultDirectory, StringComparison.OrdinalIgnoreCase);
    internal static string InstanceSuffix { get; } = IsIsolated
        ? "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())))[..16]
        : "";

    private static string ResolveDataDirectory()
    {
        var path = Environment.GetEnvironmentVariable("SNAPACTIONS_DATA_DIR");
        return string.IsNullOrWhiteSpace(path)
            ? DefaultDirectory
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
