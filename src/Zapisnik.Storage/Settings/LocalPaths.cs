namespace Zapisnik.Storage.Settings;

/// <summary>Machine-local, non-synchronised locations (<c>%LOCALAPPDATA%\Zapisnik</c>).</summary>
public sealed class LocalPaths(string baseDirectory)
{
    public static LocalPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zapisnik"));

    public string BaseDirectory { get; } = baseDirectory;

    public string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    public string IndexDatabase => Path.Combine(BaseDirectory, "index.db");

    public string LogsDirectory => Path.Combine(BaseDirectory, "logs");

    public string ModelsDirectory => Path.Combine(BaseDirectory, "models");

    public string CrashDumpsDirectory => Path.Combine(BaseDirectory, "crashes");
}
