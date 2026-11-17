using WolfMark.App.Infrastructure;

namespace WolfMark.Desktop.Infrastructure;

/// <summary>
/// Start at sign-in for this copy of the app (the twin of the Windows app's Run key): on Linux an XDG autostart
/// entry, on macOS a launch agent. The file is the source of truth – the system's own "startup applications" /
/// "login items" can remove or disable it too. Both start the app with <see cref="LaunchCommand.AutostartSwitch"/>.
/// </summary>
internal static class Autostart
{
    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

    private static string Id => InstanceIdentity.Hash(Path.GetDirectoryName(Exe)!);

    private static string Label => "app.wolfmark." + Id;

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string EntryFile => OperatingSystem.IsMacOS()
        ? Path.Combine(Home, "Library", "LaunchAgents", Label + ".plist")
        : Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(Home, ".config"),
            "autostart",
            "wolfmark-" + Id + ".desktop");

    /// <summary>The entry exists and starts this exe (a moved app has to switch it on again).</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                return IsSupported && File.Exists(EntryFile) && File.ReadAllText(EntryFile).Contains(Exe, StringComparison.Ordinal);
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
            var demo = LaunchOptions.Current.DemoDirectory;
            Directory.CreateDirectory(Path.GetDirectoryName(EntryFile)!);
            File.WriteAllText(EntryFile, OperatingSystem.IsMacOS()
                ? LaunchCommand.LaunchAgentPlist(Label, Exe, demo)
                : LaunchCommand.AutostartEntry(Exe, demo));
        }
        else if (File.Exists(EntryFile))
        {
            File.Delete(EntryFile);
        }
    }
}
