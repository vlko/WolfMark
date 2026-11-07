using System.Text.Json;
using WolfMark.Core.Settings;

namespace WolfMark.Storage.Settings;

/// <summary>Why portable mode cannot be switched on from this copy of the app.</summary>
public enum PortableUnavailableReason
{
    None,

    /// <summary>The executable's folder is not writable (e.g. Program Files).</summary>
    NotWritable,

    /// <summary>A macOS app bundle – its folder is signed and replaced on update.</summary>
    AppBundle,
}

/// <summary>
/// Portable mode (ADR 0009): <see cref="MarkerFileName"/> next to the executable names the data folder that holds
/// settings, index (<c>local/</c>) and notes (<c>notes/</c>). Without the marker the app uses the user profile.
/// </summary>
public static class PortableMode
{
    public const string MarkerFileName = "WolfMark.portable.json";

    public const string DefaultDataDirectoryName = "WolfMarkData";

    /// <summary>Settings, index, logs: the <see cref="LocalPaths"/> base inside the data folder.</summary>
    public const string LocalFolderName = "local";

    /// <summary>The notes root inside the data folder (stored relative in the settings).</summary>
    public const string NotesFolderName = "notes";

    private static readonly Lazy<string?> Detected = new(() => Detect(AppDirectory));

    /// <summary>The folder of the running executable.</summary>
    public static string AppDirectory => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    /// <summary>The portable data folder of this run, or null in standard mode. Read once per process.</summary>
    public static string? DataDirectory => Detected.Value;

    public static bool IsActive => DataDirectory is not null;

    public static string MarkerPath(string appDirectory) => Path.Combine(appDirectory, MarkerFileName);

    public static string DefaultDataDirectory(string appDirectory) => Path.Combine(appDirectory, DefaultDataDirectoryName);

    /// <summary>
    /// The data folder named by the marker in <paramref name="appDirectory"/>, or null without a marker.
    /// A damaged marker throws: silently falling back to the user profile would show an empty app and write
    /// the notes outside the portable folder.
    /// </summary>
    /// <exception cref="InvalidDataException">The marker exists but cannot be read.</exception>
    public static string? Detect(string appDirectory)
    {
        var marker = MarkerPath(appDirectory);
        if (!File.Exists(marker))
        {
            return null;
        }

        PortableMarker? value;
        try
        {
            value = JsonSerializer.Deserialize(File.ReadAllBytes(marker), WolfMarkJsonContext.Default.PortableMarker);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The portable mode file '{marker}' is invalid: {ex.Message}", ex);
        }

        var data = string.IsNullOrWhiteSpace(value?.DataDirectory) ? DefaultDataDirectoryName : value.DataDirectory;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(appDirectory, data)));
    }

    /// <summary>Writes the marker atomically; the data folder is stored relative when it lies under the app folder.</summary>
    public static Task WriteMarkerAsync(string appDirectory, string dataDirectory, CancellationToken ct)
    {
        var stored = PathScope.IsSameOrUnder(dataDirectory, appDirectory) ? Path.GetRelativePath(appDirectory, dataDirectory) : dataDirectory;
        var json = JsonSerializer.Serialize(new PortableMarker { DataDirectory = stored }, WolfMarkJsonContext.Default.PortableMarker);
        return AtomicFile.WriteAllTextAsync(MarkerPath(appDirectory), json, ct);
    }

    /// <summary>Whether portable mode can be switched on for an app in <paramref name="appDirectory"/> (touches the disk).</summary>
    public static PortableUnavailableReason CheckAvailability(string appDirectory)
    {
        if (OperatingSystem.IsMacOS() && appDirectory.Contains(".app/Contents/", StringComparison.Ordinal))
        {
            return PortableUnavailableReason.AppBundle;
        }

        var probe = Path.Combine(appDirectory, "." + Guid.NewGuid().ToString("N") + AtomicFile.TempSuffix);
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe); // our own empty probe file, not a note
            return PortableUnavailableReason.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PortableUnavailableReason.NotWritable;
        }
    }
}

/// <summary>Path containment with the platform's case rules.</summary>
public static class PathScope
{
    public static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool IsSame(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), Comparison);

    /// <summary><paramref name="path"/> equals <paramref name="directory"/> or lies inside it.</summary>
    public static bool IsSameOrUnder(string path, string directory)
    {
        var p = Normalize(path);
        var d = Normalize(directory);
        return string.Equals(p, d, Comparison) || p.StartsWith(d + Path.DirectorySeparatorChar, Comparison);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
