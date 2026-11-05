namespace WolfMark.App.Infrastructure;

/// <summary>
/// Command-line switches. <c>--demo &lt;dir&gt;</c> runs on isolated demo data (notes, Claude Code transcripts,
/// settings and index all under that folder – the user's notes and settings are never touched);
/// <c>--screenshots &lt;dir&gt;</c> additionally captures the README screenshots there and exits;
/// <c>--trace-editor</c> times the editor's hot paths into <c>logs/editor-trace.txt</c>.
/// A file path (the first argument that is not a switch) opens that Markdown file: a note of the notes tree in
/// the main window, any other file in its own window – this is what the file association runs.
/// </summary>
internal sealed record LaunchOptions(string? DemoDirectory, string? ScreenshotDirectory, bool TraceEditor = false, string? OpenFile = null)
{
    public static LaunchOptions Current { get; } = Parse(Environment.GetCommandLineArgs());

    public bool IsDemo => DemoDirectory is not null;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? demo = null, shots = null, file = null;
        var trace = false;
        // args[0] is the program itself.
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            var hasValue = i + 1 < args.Count;
            if (string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase) && hasValue)
            {
                demo = Path.GetFullPath(args[++i]);
            }
            else if (string.Equals(arg, "--screenshots", StringComparison.OrdinalIgnoreCase) && hasValue)
            {
                shots = Path.GetFullPath(args[++i]);
            }
            else if (string.Equals(arg, "--trace-editor", StringComparison.OrdinalIgnoreCase))
            {
                trace = true;
            }
            else if (file is null && !arg.StartsWith('-') && arg.Trim().Length > 0) // not a switch (nor macOS's old -psn_…)
            {
                file = Path.GetFullPath(arg);
            }
        }

        // Screenshots only ever come from demo data.
        if (shots is not null && demo is null)
        {
            demo = Path.Combine(Path.GetTempPath(), "WolfMarkDemo");
        }

        return new LaunchOptions(demo, shots, trace, file);
    }
}
