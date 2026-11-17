namespace WolfMark.App.Infrastructure;

/// <summary>
/// Command lines that start this very app again – for the registry, a system shortcut or a launcher. A demo run
/// keeps its <c>--demo</c> folder, so whatever it registers opens the demo and not the real notes.
/// </summary>
internal static class LaunchCommand
{
    /// <summary><c>"exe" [--demo "dir"] "argument"</c> – everything quoted, as the Windows registry wants it.</summary>
    public static string Quoted(string exe, string? demoDirectory, string argument) => demoDirectory is null
        ? $"\"{exe}\" \"{argument}\""
        : $"\"{exe}\" --demo \"{demoDirectory}\" \"{argument}\"";

    /// <summary>The same for the running process.</summary>
    public static string Quoted(string argument) => Quoted(
        Environment.ProcessPath ?? throw new InvalidOperationException("No process path."),
        LaunchOptions.Current.DemoDirectory,
        argument);

    /// <summary><c>exe [--demo dir] --quick-note</c> for a shortcut of the system: quotes only where a space needs them.</summary>
    public static string QuickNote(string? exe, string? demoDirectory)
    {
        static string Quote(string arg) => arg.Contains(' ', StringComparison.Ordinal) ? "\"" + arg + "\"" : arg;
        var command = Quote(exe ?? "wolfmark");
        if (demoDirectory is not null)
        {
            command += " --demo " + Quote(demoDirectory);
        }

        return command + " " + LaunchOptions.QuickNoteSwitch;
    }

    /// <summary>
    /// The XDG autostart entry (<c>~/.config/autostart/*.desktop</c>) that starts this app when the user signs in on
    /// Linux – what the Run key is on Windows.
    /// </summary>
    public static string AutostartEntry(string exe, string? demoDirectory) => string.Join('\n',
        "[Desktop Entry]",
        "Type=Application",
        "Name=WolfMark",
        "Comment=Daily notes in Markdown",
        $"Exec=\"{exe}\"{(demoDirectory is null ? string.Empty : $" --demo \"{demoDirectory}\"")}",
        "Terminal=false",
        "X-GNOME-Autostart-enabled=true",
        string.Empty);

    /// <summary>The same for the running process.</summary>
    public static string QuickNote() => QuickNote(Environment.ProcessPath, LaunchOptions.Current.DemoDirectory);
}
