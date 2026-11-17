namespace WolfMark.App.Infrastructure;

/// <summary>
/// Command lines that start this very app again – for the registry, a system shortcut or a launcher. A demo run
/// keeps its <c>--demo</c> folder, so whatever it registers opens the demo and not the real notes.
/// </summary>
internal static class LaunchCommand
{
    /// <summary>
    /// Added by what starts the app at sign-in (the Run key on Windows, the autostart entry on Linux, the launch
    /// agent on macOS): with "close to the notification area" on, such a start stays hidden there.
    /// </summary>
    public const string AutostartSwitch = "--autostart";

    /// <summary>This process was started by the system at sign-in, not by the user.</summary>
    public static bool LaunchedAtSignIn { get; } =
        Environment.GetCommandLineArgs().Contains(AutostartSwitch, StringComparer.OrdinalIgnoreCase);

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
        $"Exec=\"{exe}\"{(demoDirectory is null ? string.Empty : $" --demo \"{demoDirectory}\"")} {AutostartSwitch}",
        "Terminal=false",
        "X-GNOME-Autostart-enabled=true",
        string.Empty);

    /// <summary>
    /// The launch agent (<c>~/Library/LaunchAgents/&lt;label&gt;.plist</c>) that starts this app when the user logs
    /// in on macOS. Takes effect at the next login; removing the file switches it off.
    /// </summary>
    public static string LaunchAgentPlist(string label, string exe, string? demoDirectory)
    {
        static string Xml(string s) => System.Security.SecurityElement.Escape(s);
        var arguments = new List<string> { exe };
        if (demoDirectory is not null)
        {
            arguments.Add("--demo");
            arguments.Add(demoDirectory);
        }

        arguments.Add(AutostartSwitch);
        return string.Join('\n',
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
            "<plist version=\"1.0\">",
            "<dict>",
            "  <key>Label</key>",
            $"  <string>{Xml(label)}</string>",
            "  <key>ProgramArguments</key>",
            "  <array>",
            string.Join('\n', arguments.Select(a => $"    <string>{Xml(a)}</string>")),
            "  </array>",
            "  <key>RunAtLoad</key>",
            "  <true/>",
            "  <key>ProcessType</key>",
            "  <string>Interactive</string>",
            "</dict>",
            "</plist>",
            string.Empty);
    }

    /// <summary>The same for the running process.</summary>
    public static string QuickNote() => QuickNote(Environment.ProcessPath, LaunchOptions.Current.DemoDirectory);
}
