using System.Security.Cryptography;
using System.Text;
using WolfMark.Storage.Settings;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// One running WolfMark per data folder (settings, index): a second start – also one from another copy of the
/// app, e.g. a development build – hands its file over to the instance that owns the data and ends, so two
/// processes never write the same index. A run with other data (<c>--demo</c>) is its own instance.
/// </summary>
internal static class InstanceIdentity
{
    /// <summary>The data folder of this run: <c>&lt;demo&gt;/local</c> or the user's local app data.</summary>
    public static string DataDirectory(LaunchOptions options) =>
        options.DemoDirectory is { } demo ? Path.Combine(demo, "local") : LocalPaths.Default.BaseDirectory;

    /// <summary>"WolfMark.3f2a…": the same for every spelling of the same folder (case on Windows, trailing separator).</summary>
    public static string Key(string dataDirectory) => "WolfMark." + Hash(dataDirectory);

    /// <summary>12 hex digits of SHA-256 of the normalised full path.</summary>
    public static string Hash(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OperatingSystem.IsWindows())
        {
            full = full.ToUpperInvariant();
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12];
    }
}
