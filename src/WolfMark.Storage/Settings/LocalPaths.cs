namespace WolfMark.Storage.Settings;

/// <summary>
/// Machine-local, non-synchronised locations: <c>%LOCALAPPDATA%\WolfMark</c>, or <c>&lt;data&gt;/local</c> in
/// portable mode (ADR 0009).
/// </summary>
/// <param name="portableDataDirectory">The portable data folder; relative paths in the settings resolve against it.</param>
public sealed class LocalPaths(string baseDirectory, string? portableDataDirectory = null)
{
    // Create: without it a missing folder (no ~/.local/share on a fresh Linux) comes back as "" and the data
    // would land in the current directory.
    private static readonly Lazy<LocalPaths> StandardPaths = new(() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "WolfMark")));

    private static readonly Lazy<LocalPaths> DefaultPaths = new(() =>
        PortableMode.DataDirectory is { } data ? ForPortable(data) : StandardPaths.Value);

    /// <summary>The locations of this run: portable when the marker is present, otherwise <see cref="Standard"/>.</summary>
    public static LocalPaths Default => DefaultPaths.Value;

    /// <summary>The user-profile locations, whatever mode this run is in (target when leaving portable mode).</summary>
    public static LocalPaths Standard => StandardPaths.Value;

    public static LocalPaths ForPortable(string dataDirectory) =>
        new(Path.Combine(dataDirectory, PortableMode.LocalFolderName), dataDirectory);

    public string BaseDirectory { get; } = baseDirectory;

    /// <summary>Null in standard mode.</summary>
    public string? PortableDataDirectory { get; } = portableDataDirectory;

    public string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    public string IndexDatabase => Path.Combine(BaseDirectory, "index.db");

    public string LogsDirectory => Path.Combine(BaseDirectory, "logs");

    public string ModelsDirectory => Path.Combine(BaseDirectory, "models");

    /// <summary>Downscaled image previews of the editor (a cache – safe to delete).</summary>
    public string PreviewCacheDirectory => Path.Combine(BaseDirectory, "cache", "previews");

    public string CrashDumpsDirectory => Path.Combine(BaseDirectory, "crashes");
}
