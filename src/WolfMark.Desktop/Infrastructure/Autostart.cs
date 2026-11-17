using WolfMark.App.Infrastructure;

namespace WolfMark.Desktop.Infrastructure;

/// <summary>
/// Start at sign-in on Linux: an XDG autostart entry for this copy of the app (the twin of the Windows app's Run
/// key). The file is the source of truth – the desktop's own "startup applications" can remove it too. macOS has no
/// such file; there the user adds the app to Login Items.
/// </summary>
internal static class Autostart
{
    public static bool IsSupported => OperatingSystem.IsLinux();

    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

    private static string EntryFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart",
        "wolfmark-" + InstanceIdentity.Hash(Path.GetDirectoryName(Exe)!) + ".desktop");

    /// <summary>The entry exists and starts this exe (a moved app has to switch it on again).</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                return IsSupported && File.Exists(EntryFile) && File.ReadAllText(EntryFile).Contains($"\"{Exe}\"", StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        if (!IsSupported)
        {
            return;
        }

        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EntryFile)!);
            File.WriteAllText(EntryFile, LaunchCommand.AutostartEntry(Exe, LaunchOptions.Current.DemoDirectory));
        }
        else if (File.Exists(EntryFile))
        {
            File.Delete(EntryFile);
        }
    }
}
