namespace WolfMark.Storage.Settings;

/// <summary>Machine-local, non-synchronised locations (<c>%LOCALAPPDATA%\WolfMark</c>).</summary>
public sealed class LocalPaths(string baseDirectory)
{
    // Create: without it a missing folder (no ~/.local/share on a fresh Linux) comes back as "" and the data
    // would land in the current directory.
    public static LocalPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "WolfMark"));

    public string BaseDirectory { get; } = baseDirectory;

    public string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    public string IndexDatabase => Path.Combine(BaseDirectory, "index.db");

    public string LogsDirectory => Path.Combine(BaseDirectory, "logs");

    public string ModelsDirectory => Path.Combine(BaseDirectory, "models");

    /// <summary>Downscaled image previews of the editor (a cache – safe to delete).</summary>
    public string PreviewCacheDirectory => Path.Combine(BaseDirectory, "cache", "previews");

    public string CrashDumpsDirectory => Path.Combine(BaseDirectory, "crashes");
}
