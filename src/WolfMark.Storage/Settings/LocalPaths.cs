namespace WolfMark.Storage.Settings;

/// <summary>Machine-local, non-synchronised locations (<c>%LOCALAPPDATA%\WolfMark</c>).</summary>
public sealed class LocalPaths(string baseDirectory)
{
    public static LocalPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WolfMark"));

    public string BaseDirectory { get; } = baseDirectory;

    public string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    public string IndexDatabase => Path.Combine(BaseDirectory, "index.db");

    public string LogsDirectory => Path.Combine(BaseDirectory, "logs");

    public string ModelsDirectory => Path.Combine(BaseDirectory, "models");

    /// <summary>Downscaled image previews of the editor (a cache – safe to delete).</summary>
    public string PreviewCacheDirectory => Path.Combine(BaseDirectory, "cache", "previews");

    public string CrashDumpsDirectory => Path.Combine(BaseDirectory, "crashes");
}
